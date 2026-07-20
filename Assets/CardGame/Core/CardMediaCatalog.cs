using System;
using System.Collections.Generic;

namespace CardGame.Core
{
    public static class CardMediaCatalog
    {
        public const string ImageHost = "https://img.duelmasters.us";
        public const string VideoHost = "https://media.duelmasters.us";

        private static readonly HashSet<int> VideoIds = new HashSet<int>
        {
            56, 73, 77, 80, 87, 90, 95, 155, 157, 168
        };

        public static int MediaId(string setCode, string collectorNumber)
        {
            if (string.IsNullOrWhiteSpace(setCode) || string.IsNullOrWhiteSpace(collectorNumber))
                return 0;

            var slash = collectorNumber.IndexOf('/');
            var numberText = slash < 0 ? collectorNumber : collectorNumber.Substring(0, slash);
            if (!int.TryParse(numberText, out var collector)) return 0;

            if (setCode.Equals("DM-01", StringComparison.OrdinalIgnoreCase)) return collector + 10;
            if (setCode.Equals("DM-02", StringComparison.OrdinalIgnoreCase)) return collector + 125;
            return 0;
        }

        public static bool HasVideo(int mediaId) => VideoIds.Contains(mediaId);

        public static string ImageUrl(int mediaId) =>
            mediaId > 0 ? $"{ImageHost}/{mediaId:0000}.webp" : string.Empty;

        public static string VideoUrl(int mediaId) =>
            mediaId > 0 ? $"{VideoHost}/{mediaId:0000}.mp4" : string.Empty;
    }
}
