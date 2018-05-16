using System;
using System.Collections.Generic;
using System.Net;

namespace wmcards
{
    public class CardItem
    {
        internal int Id { get; private set; }
        internal string Faction { get; private set; }
        internal string Job { get; private set; }

        public IReadOnlyList<string> Images { get; private set; }
        public string Title { get; private set; }

        public CardItem(int id, string faction, string job, string title)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (string.IsNullOrWhiteSpace(faction))
            {
                throw new ArgumentNullException(nameof(faction));
            }

            if (string.IsNullOrWhiteSpace(job))
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentNullException(nameof(title));
            }

            Id = id;
            Faction = faction;
            Job = job;
            Title = EscapeHtmlEntities(title);
        }

        public void UpdateImageNames(IReadOnlyList<string> imageLocations)
        {
            if (imageLocations.Count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(imageLocations));
            }

            Images = imageLocations;
        }

        private static string EscapeHtmlEntities(string value)
        {
            value = WebUtility.HtmlDecode(value);
            return value.Replace("&comma;", ","); // this is bad but yeah
        }
    }
}