﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SecureIheTransactionTests.cs" company="Reimers.dk">
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

namespace Reimers.Ihe.Communication.Tests
{
    using System;
    using System.Net;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading.Tasks;
    using NHapi.Base.Parser;
    using NHapi.Model.V251.Message;
    using Xunit;

    public class SecureIheTransactionTests : IDisposable
    {
        private readonly MllpServer _server;
        private int _port = 2576;
        private readonly X509Certificate2Collection _cert;

        public SecureIheTransactionTests()
        {
            _cert = new X509Certificate2Collection(new X509Certificate2("cert.pfx", "password"));
            _server = new MllpServer(new IPEndPoint(IPAddress.Loopback, _port), new TestMiddleware(), serverCertificate: _cert[0], userCertificateValidationCallback: UserCertificateValidationCallback);
            _server.Start();
        }

        private bool UserCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        [Fact]
        public async Task WhenSendingMessageThenGetsAck()
        {
            var connectionFactory = new DefaultMllpConnectionFactory(IPAddress.Loopback.ToString(), _port, clientCertificateCollection: _cert, userCertificateValidationCallback: UserCertificateValidationCallback);
            var client = new TestTransaction(connectionFactory.Get, new PipeParser());
            var request = new QBP_Q11();
            var response = await client.Send(request); Assert.NotNull(response);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _server?.Dispose();
        }
    }
}