using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Helpers.Net.Extensions
{
    public static class StringExtension
    {
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        /// <summary>
        /// Checks string object's value to array of string values
        /// </summary>
        /// <param name="value"></param>
        /// <param name="stringValues">Array of string values to compare</param>
        /// <returns>Return true if any string value matches</returns>
        public static bool In(this string value, params string[] stringValues)
        {
            foreach (string otherValue in stringValues)
                if (string.CompareOrdinal(value, otherValue) == 0)
                    return true;

            return false;
        }

        /// <summary>
        /// Converts string to enum object
        /// </summary>
        /// <typeparam name="T">Type of enum</typeparam>
        /// <param name="value">String value to convert</param>
        /// <returns>Returns enum object</returns>
        public static T ToEnum<T>(this string value)
            where T : struct
        {
            return (T)Enum.Parse(typeof(T), value, true);
        }

        /// <summary>
        /// Returns characters from right of specified length
        /// </summary>
        /// <param name="value">String value</param>
        /// <param name="length">Max number of charaters to return</param>
        /// <returns>Returns string from right</returns>
        public static string Right(this string value, int length)
        {
            return value != null && value.Length > length ? value.Substring(value.Length - length) : value;
        }

        /// <summary>
        /// Returns characters from left of specified length
        /// </summary>
        /// <param name="value">String value</param>
        /// <param name="length">Max number of charaters to return</param>
        /// <returns>Returns string from left</returns>
        public static string Left(this string value, int length)
        {
            return value != null && value.Length > length ? value.Substring(0, length) : value;
        }

        public static string RemoveIllegalFileCharacters(this string value)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());

            foreach (char c in invalid)
            {
                value = value.Replace(c.ToString(), string.Empty);
            }

            return value;
        }

        public static string RemoveSpaces(this string value)
        {
            return value.Replace(" ", string.Empty);
        }

        public static string RemoveAccentCharacters(this string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var encoding = Encoding.GetEncoding("iso-8859-8");
                var buffer = encoding.GetBytes(value);
                value = encoding.GetString(buffer);
            }

            return value;
        }

        public static string UnconvertToHtmlSpecialCharacters(this string value)
        {
            var specialCharacters = new Dictionary<string, string>();

            //& must be first
            specialCharacters.Add("&amp;", "&");
            specialCharacters.Add("&lt;", "<");
            specialCharacters.Add("&gt;", ">");

            string returnValue = value;
            foreach (var special in specialCharacters)
            {
                returnValue = returnValue.Replace(special.Key, special.Value);
            }

            return returnValue;
        }

        public static string InitCap(this string s)
        {
            // Check for empty string.
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            // Return char and concat substring.
            return char.ToUpper(s[0]) + s.Substring(1).ToLower();
        }

        public static string CamelCase(this string s)
        {
            return Regex.Replace(s, @"([A-Z])([A-Z]+|[a-z0-9_]+)($|[A-Z]\w*)",
            m =>
            {
                return m.Groups[1].Value.ToLower() + m.Groups[2].Value.ToLower() + m.Groups[3].Value;
            });
        }
    }
}
