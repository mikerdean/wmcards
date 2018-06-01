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
        public bool? Attachment { get; private set; }
        public int? BonusPoints { get; private set; }
        public int? Points { get; private set; }
        public int? PointsMinimum { get; private set; }
        public int? PointsMaximum { get; private set; }
        public int? SizeMinimum { get; private set; }
        public int? SizeMaximum { get; private set; }
        public int? FieldAllowance { get; private set; }

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

        public void UpdateAttachment()
        {
            Attachment = true;
        }

        public void UpdateFieldAllowance(int fieldAllowance)
        {
            if (fieldAllowance < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldAllowance));
            }

            FieldAllowance = fieldAllowance;
        }

        public void UpdatePoints(int points)
        {
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points));
            }

            if (Job.Equals("Warcaster", StringComparison.OrdinalIgnoreCase) || Job.Equals("Warlock", StringComparison.OrdinalIgnoreCase))
            {
                BonusPoints = points;
                Points = 0;
            }
            else
            {
                Points = points;
            }
        }

        public void UpdatePointsMin(int points)
        {
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points));
            }

            PointsMinimum = points;
        }

        public void UpdatePointsMax(int points)
        {
            if (points < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(points));
            }

            PointsMaximum = points;
        }

        public void UpdateSizeMin(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            SizeMinimum = size + 1;
        }

        public void UpdateSizeMax(int size)
        {
            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            SizeMaximum = size + 1;
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

        public override string ToString()
        {
            return Title ?? "Unknown";
        }
    }
}