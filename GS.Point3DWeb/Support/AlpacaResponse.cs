/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GS.Alpaca.Support
{
    public class AlpacaResponse
    {
        public string MethodName { get; set; } = string.Empty;
        public string MethodType { get; set; } = string.Empty;
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;

        // Alpaca transaction IDs are unsigned 32-bit, so they can pass int.MaxValue.
        [JsonProperty($"ClientTransactionID")] public long ClientTransactionId { get; set; }
        [JsonProperty($"ServerTransactionID")] public long ServerTransactionId { get; set; }
        [JsonProperty($"ErrorNumber")] public int ErrorNumber { get; set; }
        [JsonProperty($"ErrorMessage")] public string? ErrorMessage { get; set; }
        [JsonProperty($"Value")] public required JToken Value { get; set; }
    }
}
