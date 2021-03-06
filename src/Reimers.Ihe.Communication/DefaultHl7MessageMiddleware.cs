// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DefaultHl7MessageMiddleware.cs" company="Reimers.dk">
//   Copyright � Reimers.dk 2017
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
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using NHapi.Base.Parser;

    /// <summary>
    /// Defines the public interface for middleware for handling HL7 messages.
    /// </summary>
    public class DefaultHl7MessageMiddleware : IHl7MessageMiddleware
    {
        private readonly PipeParser _parser;
        private readonly Dictionary<string, IIheTransactionHandler> _handlers;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultHl7MessageMiddleware"/> class.
        /// </summary>
        /// <param name="handlers">The message handlers to use.</param>
        /// <param name="parser">the message parser.</param>
        public DefaultHl7MessageMiddleware(PipeParser parser = null, params IIheTransactionHandler[] handlers)
        {
            _parser = parser ?? new PipeParser();
            _handlers = handlers.ToDictionary(x => x.Version + x.Handles, x => x);
        }

        /// <summary>
        /// Handles the passed <see cref="Hl7Message"/> message.
        /// </summary>
        /// <param name="message">The <see cref="Hl7Message"/> to handle.</param>
        /// <param name="cancellation"></param>
        /// <returns>An HL7 response as a <see cref="string"/>.</returns>
        public async Task<string> Handle(
            Hl7Message message,
            CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var msg = _parser.Parse(message.Message);
            var structureName = msg.GetStructureName();
            var handler = _handlers[msg.Version + structureName];
            var response = await handler.Handle(msg, cancellation).ConfigureAwait(false);

            return _parser.Encode(response);
        }
    }
}