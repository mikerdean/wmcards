using System;
using System.Net;

namespace wmcards
{
    public struct CardItem
    {
        public readonly int Id;
        public readonly string Faction;
        public readonly string Job;
        public readonly string Title;

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

        private static string EscapeHtmlEntities(string value)
        {
            value = WebUtility.HtmlDecode(value);
            return value.Replace("&comma;", ","); // this is bad but yeah
        }
    }
}