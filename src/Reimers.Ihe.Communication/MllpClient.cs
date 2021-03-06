﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MllpClient.cs" company="Reimers.dk">
//   Copyright © Reimers.dk 2017
//   This source is subject to the MIT License.
//   Please see https://opensource.org/licenses/MIT for details.
//   All other rights reserved.
//
//   The above copyright notice and this permission notice shall be included in
//   all copies or substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//   THE SOFTWARE.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Reimers.Ihe.Communication
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the <see cref="MllpClient"/> class.
    /// </summary>
    public class MllpClient : IHostConnection
    {
        private readonly string _address;
        private readonly int _port;
        private readonly IMessageLog _messageLog;
        private readonly Encoding _encoding;
        private readonly X509CertificateCollection _clientCertificates;

        private readonly RemoteCertificateValidationCallback
            _userCertificateValidationCallback;

        private string _remoteAddress;

        private readonly TaskCompletionSource<Hl7Message> _completionSource =
            new TaskCompletionSource<Hl7Message>();

        private Stream _stream;

        private readonly CancellationTokenSource _tokenSource =
            new CancellationTokenSource();

        private TcpClient _tcpClient;
        private Task _readThread;
        private bool _sending;

        private MllpClient(
            string address,
            int port,
            IMessageLog messageLog,
            Encoding encoding,
            X509CertificateCollection clientCertificates,
            RemoteCertificateValidationCallback
                userCertificateValidationCallback)
        {
            _address = address;
            _port = port;
            _messageLog = messageLog;
            _encoding = encoding;
            _clientCertificates = clientCertificates;
            _userCertificateValidationCallback =
                userCertificateValidationCallback;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="MllpClient"/> class.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="messageLog"></param>
        /// <param name="encoding"></param>
        /// <param name="clientCertificates"></param>
        /// <param name="userCertificateValidationCallback"></param>
        /// <returns></returns>
        public static async Task<IHostConnection> Create(
            string address,
            int port,
            IMessageLog messageLog,
            Encoding encoding,
            X509CertificateCollection clientCertificates = null,
            RemoteCertificateValidationCallback
                userCertificateValidationCallback = null)
        {
            var instance = new MllpClient(
                address,
                port,
                messageLog,
                encoding,
                clientCertificates,
                userCertificateValidationCallback);
            await instance.Setup().ConfigureAwait(false);
            return instance;
        }

        /// <summary>
        /// Sends the HL7 message to the server.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> for the async operation.</param>
        /// <returns></returns>
        public async Task<Hl7Message> Send(
            string message,
            CancellationToken cancellationToken = default)
        {
            lock (_stream)
            {
                if (_sending)
                {
                    throw new InvalidOperationException("Transaction ongoing");
                }

                _sending = true;
            }

            var bytes = _encoding.GetBytes(message);
            var length = bytes.Length
                         + Constants.StartBlock.Length
                         + Constants.EndBlock.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            Array.Copy(
                Constants.StartBlock,
                0,
                buffer,
                0,
                Constants.StartBlock.Length);
            Array.Copy(
                bytes,
                0,
                buffer,
                Constants.StartBlock.Length,
                bytes.Length);
            Array.Copy(
                Constants.EndBlock,
                0,
                buffer,
                Constants.StartBlock.Length + bytes.Length,
                Constants.EndBlock.Length);

            await _messageLog.Write(message).ConfigureAwait(false);
            await _stream.WriteAsync(buffer, 0, length, cancellationToken)
                .ConfigureAwait(false);
            ArrayPool<byte>.Shared.Return(buffer);
            return await _completionSource.Task.ConfigureAwait(false);
        }

        private async Task Setup()
        {
            _tcpClient = new TcpClient(_address, _port);
            _remoteAddress = _tcpClient.Client.RemoteEndPoint.ToString();
            _stream = _tcpClient.GetStream();
            if (_clientCertificates != null)
            {
                var ssl = new SslStream(
                    _stream,
                    false,
                    _userCertificateValidationCallback);
                await ssl.AuthenticateAsClientAsync(
                        _address,
                        _clientCertificates,
                        SslProtocols.Tls11 | SslProtocols.Tls12,
                        false)
                    .ConfigureAwait(false);
                _stream = ssl;
            }

            _readThread = ReadStream(_tokenSource.Token);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _tcpClient.Close();
            _tcpClient.Dispose();
            _tokenSource.Cancel();
            _tokenSource.Dispose();
            _stream.Close();
            _stream.Dispose();
        }

        private async Task ReadStream(CancellationToken cancellationToken)
        {
            await Task.Yield();
            try
            {
                byte previous = 0;
                var messageBuilder = new List<byte>();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var current = (byte)_stream.ReadByte();

                    cancellationToken.ThrowIfCancellationRequested();
                    if (Constants.EndBlock.SequenceEqual(
                        new[] { previous, current }))
                    {
                        messageBuilder.RemoveAt(messageBuilder.Count - 1);
                        var s = _encoding.GetString(messageBuilder.ToArray());
                        var message = new Hl7Message(s, _remoteAddress);
                        _completionSource.SetResult(message);
                        break;
                    }

                    if (previous == 0 && current == 11)
                    {
                        if (messageBuilder.Count > 0)
                        {
                            _completionSource.SetException(
                                new Exception(
                                    $"Unexpected character: {current:x2}"));
                        }
                    }
                    else
                    {
                        messageBuilder.Add(current);
                        previous = current;
                    }
                }
            }
            catch (IOException io)
            {
                Trace.TraceInformation(io.Message);
                if (!cancellationToken.IsCancellationRequested)
                {
                    _completionSource.SetException(io);
                }
            }
        }
    }
}
