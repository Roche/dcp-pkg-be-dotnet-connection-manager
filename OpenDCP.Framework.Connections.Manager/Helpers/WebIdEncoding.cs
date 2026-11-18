using OpenDCP.Framework.Connections.Manager.Constants;
using OpenDCP.Framework.Exceptions;
using System.Text.RegularExpressions;

namespace OpenDCP.Framework.Connections.Manager.Helpers
{
    public static class WebIdEncoding
    {
        public static bool IsInternalWebId(string webId)
        {
            return InternalWebIdRegex.IsMatch(webId);
        }
        public static string Encode(string webId, int datasourceId,
            int version = 1, WebIdType format = WebIdType.External)
        {
            if (version != 1)
            {
                throw new DCPBadRequestException("The webId version is not supported");
            }

            if (format == WebIdType.External || (WebIdType.DCP == format && IsInternalWebId(webId)))
            {
                return webId;
            }

            if (datasourceId > Math.Pow(36, 5))
            {
                throw new DCPBadRequestException("The datasource is out of range and can't be encoded");
            }

            return $"D1C{Base36Converter.ToBase36String(datasourceId)}P{webId}";
        }

        public static WebIdType Format(string webId)
        {
            if (webId.StartsWith("D1"))
            {
                return WebIdType.DCP;
            }
            else
            {
                return WebIdType.External;
            }
        }

        public static (string WebId, int DatasourceId, int Version, WebIdType Format) Decode(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded))
                throw new ArgumentException("Encoded string cannot be null or empty", nameof(encoded));

            // If it doesn't start with D1, treat as External
            if (!encoded.StartsWith("D1"))
            {
                return (encoded, default, 1, WebIdType.External);
            }

            int version = 1;
            WebIdType format = WebIdType.DCP;

            // 2 = "D1"
            int dataTypeStart = 2;

            // DataType is a single byte (string of digits/letters) until "C"
            int cIndex = encoded.IndexOf('C', dataTypeStart);
            if (cIndex == -1)
                throw new FormatException("Encoded string missing 'C' separator");

            // After 'C' comes 5-char Base36 datasourceId
            if (encoded.Length < cIndex + 1 + 5)
                throw new FormatException("Encoded string too short to contain datasourceId");

            string base36Id = encoded.Substring(cIndex + 1, 5);
            int datasourceId = Base36Converter.FromBase36String(base36Id);

            // After that should be 'P' separator
            int pIndex = cIndex + 1 + 5;
            if (pIndex >= encoded.Length || encoded[pIndex] != 'P')
                throw new FormatException("Encoded string missing 'P' separator");

            // Everything after 'P' is webId
            string webId = encoded.Substring(pIndex + 1);

            return (webId, datasourceId, version, format);
        }

        private static readonly Regex InternalWebIdRegex =
                new Regex(@"^D1C[0-9A-Z]+P[A-Za-z0-9_-]+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(10));
    }
}
