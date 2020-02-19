using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Helpers.Net.Extensions
{
	public class EncodedString
	{

		#region Quoted String Encoding
		
		public static bool IsQuotedStringEncodingRequired(string source, bool allowOnlyWhitespace = false, bool allowSpecialWhitespace = false, bool allowUnicode=false, bool quoteQuotes = false, char delimiter='\0')
		{
			if (string.IsNullOrEmpty(source))
				return false;

			bool nonWhitespace = false;

			foreach (var c in source)
			{
				if (!char.IsWhiteSpace(c))
					nonWhitespace = true;

				if (c == delimiter)
					return true;

				if (!allowSpecialWhitespace)
				{
					switch (c)
					{
						case '"':
							if (!nonWhitespace || quoteQuotes)
								return true;
							break;
						case '\r':
						case '\n':
						case '\t':
						case '\0':
							return true;
					}
				}

				if (!allowUnicode)
				{
					if (c <= 31 || c >= 128)
						return true;
				}
			}

			if (allowOnlyWhitespace)
				return false;

			if (!nonWhitespace)
				return true;
			
			return false;
		}

		public static string EncodeQuotedString(string source, bool allowUnicode=false)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append('"');
			if (!string.IsNullOrEmpty(source))
			{
				foreach (var c in source)
				{
					switch (c)
					{
						case '\\':
							sb.Append(@"\\");
							break;
						case '\r':
							sb.Append(@"\r");
							break;
						case '\n':
							sb.Append(@"\n");
							break;
						case '\t':
							sb.Append(@"\t");
							break;
						case '\0':
							sb.Append(@"\0");
							break;
						case '"':
							sb.Append("\\\"");
							break;
						default:
							if (c > 31 && c < 128)
							{
								sb.Append(c);
							}
							else
							{
								if (allowUnicode)
								{
									sb.Append(c);
								}
								else
								{
									// TODO: Need to handle surrogate pair encoding
									sb.Append(@"\u");
									sb.Append(((int)c).ToString("x4"));
								}
							}
							break;
					}
				}
			}
			sb.Append('"');
			return sb.ToString();
		}

		private static List<char> _validSepChar = new List<char> { '|', ',', ';', '~', '`', '\t' };

		private static Dictionary<char, int> _decodeHexChar = new Dictionary<char, int> { 
		{'0', 0},		{'1', 1},		{'2', 2},		{'3', 3},		{'4', 4},
		{'5', 5},		{'6', 6},		{'7', 7},		{'8', 8},		{'9', 9},
		{'a', 10},		{'b', 11},		{'c', 12},		{'d', 13},		{'e', 14},
		{'f', 15},		{'A', 10},		{'B', 11},		{'C', 12},		{'D', 13},
		{'E', 14},		{'F', 15}};

		public static string ParseQuotedString(string source)
		{
			if (source.Length > 0 && source[0] == '"')
				return ParseQuotedStrings(source, 0).FirstOrDefault();
			return source;
		}

		public static string ParseStringParameters(string source, Func<string, string> param)
		{
			if (source == null)
				return null;

			var sb = new StringBuilder();
			var parameter = new StringBuilder();

			int parseParameter = 0;
			foreach (var c in source)
			{
				if (parseParameter > 0)
				{
					if (c == '{')
					{
						parseParameter++;
					}
					else if (c == '}')
					{
						parseParameter--;
						if (parseParameter <= 0)
						{
							parseParameter = 0;
							sb.Append(param(parameter.ToString()));
							parameter.Clear();
							continue;
						}
					}

					parameter.Append(c);
				}
				else
				{
					if (c == '{')
					{
						parseParameter++;
						continue;
					}

					sb.Append(c);
				}
			}

			return sb.ToString();
		}

		public static List<string> ParseQuotedStrings(string source, int offset = 0)
		{
			List<string> result = new List<string>();

			StringBuilder sb = new StringBuilder();

			int unicodeCount = 0;
			int unicodeChar = 0;

			bool isEscaped = false;
			bool parseString = false;

			for (int i = offset; i < source.Length; i++)
			{
				char c = source[i];

				if (parseString)
				{
					if (unicodeCount > 0)
					{
						if (!_decodeHexChar.ContainsKey(c))
							throw new Exception(string.Format("Parsing error decoding unicode character: '{0}'", source));

						unicodeChar *= 16;
						unicodeChar += _decodeHexChar[c];
						unicodeCount--;
						if (unicodeCount == 0)
						{
							sb.Append(char.ConvertFromUtf32(unicodeChar));
							unicodeChar = 0;
						}
						continue;
					}

					if (isEscaped)
					{
						isEscaped = false;
						switch (c)
						{
							case 'u':
								unicodeCount = 4;
								break;
							case 'U':
								unicodeCount = 8;
								break;
							case '"':
								sb.Append('"');
								break;
							case 'R':
							case 'r':
								sb.Append('\r');
								break;
							case 'N':
							case 'n':
								sb.Append('\n');
								break;
							case 'T':
							case 't':
								sb.Append('\t');
								break;
							case '0':
								sb.Append('\0');
								break;
							case '\\':
								sb.Append('\\');
								break;

						}
					}
					else
					{
						switch (c)
						{
							case '"':
								parseString = false;
								result.Add(sb.ToString());
								sb.Clear();
								break;
							case '\\':
								isEscaped = true;
								break;
							default:
								sb.Append(c);
								break;
						}
					}
				}
				else
				{
					switch (c)
					{
						case '"':
							parseString = true;
							break;
					}
				}
			}

			return result;
		}

		#endregion

		#region Simple JSON Serializing and Parsing

		public static string Serialize(object source, bool quoteStrings = true, bool quoteDates = true, int indent=-1, bool allowUnicode=true)
		{
			var useIndent = indent >= 0;

			if (source == null)
			{
				return "";
			}

			if (source is string)
			{
				if (quoteStrings)
					return EncodeQuotedString((string)source, allowUnicode:allowUnicode);
				return (string)source;
			}

			if (source is DateTime)
			{
				if (quoteDates)
					return string.Format("\"{0}\"", ((DateTime)source).ToString("o"));
				return string.Format("{0}", ((DateTime)source).ToString("o"));
			}

			if (source is bool)
			{
				return ((bool)source) ? "true" : "false";
			}

			if (source is int || source is long || source is decimal || source is float || source is double)
			{
				return source.ToString();
			}

			if (source is Dictionary<string, object>)
			{
				return Serialize((Dictionary<string, object>)source, indent, allowUnicode: allowUnicode);
			}

			var ls = source as System.Collections.IEnumerable;
			if (ls != null)
			{
				StringBuilder sb = new StringBuilder();
				sb.Append('[');

				bool first = true;

				foreach (var obj in ls)
				{
					if (!first)
						sb.Append(',');

					first = false;
					sb.Append(Serialize(obj, indent:useIndent?indent+1:-1, allowUnicode: allowUnicode));
				}

				sb.Append(']');

				return sb.ToString();
			}

			return Serialize(source.ToString());
		}

		public static string Serialize(Dictionary<string, object> source, int indent = -1, bool allowUnicode = true)
		{
			StringBuilder sb = new StringBuilder();
			var useIndent = indent >= 0;

			if (useIndent)
				sb.AppendLine().Append(' ', indent*2);

			sb.Append('{');
			bool first = true;
			foreach (var item in source)
			{
				if (!first)
					sb.Append(',');

				first = false;

				if (useIndent) sb.AppendLine().Append(' ', (indent + 1)*2);
				sb.Append(string.Format("\"{0}\":", item.Key));

				sb.Append(Serialize(item.Value, indent:useIndent ? indent+2 : -1, allowUnicode:allowUnicode));
			}

			if (useIndent)
				sb.AppendLine().Append(' ', indent * 2);

			sb.Append('}');
			return sb.ToString();
		}

		public static Dictionary<string, object> ParseObject(string source, bool trimStrings = false)
		{
			bool quoted = false;
			bool escaped = false;
			int level = 0;

			bool parseKey = false;
			bool parseValue = false;

			StringBuilder key = new StringBuilder();
			StringBuilder item = new StringBuilder();
			Dictionary<string, object> result = new Dictionary<string, object>();

			var ts = source.Trim();
			if (ts.Length > 2 && ts[0] == '{' && ts[ts.Length - 1] == '}')
				ts = ts.Substring(1, ts.Length - 2);

			foreach (var c in ts)
			{
				if (parseValue)
				{
					if (escaped)
					{
						escaped = false;
						item.Append(c);
						continue;
					}

					if (quoted)
					{
						if (c == '\\')
							escaped = true;
						else if (c == '"')
							quoted = false;

						item.Append(c);
						continue;
					}

					switch (c)
					{
						case '"':
							quoted = true;
							break;
						case ',':
							if (level == 0)
							{
								result[key.ToString().Trim()] = Parse(item.ToString(), trimStrings:trimStrings);
								item.Clear();
								key.Clear();
								parseValue = false;
								continue;
							}
							break;
						case '{':
						case '[':
							level++;
							break;
						case '}':
						case ']':
							level--;
							break;
					}
					item.Append(c);
				}
				else if (parseKey)
				{
					if (c == '"')
						parseKey = false;
					else
						key.Append(c);
				}
				else
				{
					if (c == '"')
						parseKey = true;
					else if (c == ':')
						parseValue = true;
				}
			}

			if (parseValue && level == 0)
			{
				result[key.ToString()] = Parse(item.ToString(), trimStrings: trimStrings);
			}

			return result;
		}

		public static List<object> ParseList(string source, bool trimStrings=false)
		{
			if (source == "[]") return new List<object>();

			bool quoted = false;
			bool escaped = false;
			int level = 0;

			var ts = source.Trim();
			if (ts.Length > 2 && ts[0] == '[' && ts[ts.Length - 1] == ']')
				ts = ts.Substring(1, ts.Length - 2);

			StringBuilder item = new StringBuilder();

			List<object> result = new List<object>();
			foreach (var c in ts)
			{
				if (escaped)
				{
					escaped = false;
					item.Append(c);
					continue;
				}

				if (quoted)
				{
					if (c == '\\')
						escaped = true;
					else if (c == '"')
						quoted = false;

					item.Append(c);
					continue;
				}

				switch (c)
				{
					case '"':
						quoted = true;
						break;
					case ',':
						if (level == 0)
						{
							result.Add(Parse(item.ToString(), trimStrings:trimStrings));
							item.Clear();
							continue;
						}
						break;
					case '{':
					case '[':
						level++;
						break;
					case '}':
					case ']':
						level--;
						break;
				}
				item.Append(c);
			}

			if (level == 0)
			{
				result.Add(Parse(item.ToString(), trimStrings: trimStrings));
			}

			return result;
		}

		public static object Parse(string source, bool quoteStrings = true, bool quoteDate = true, bool trimStrings = false)
		{
			var ts = source.Trim();

			if (string.IsNullOrEmpty(ts))
				return null;

			if (ts.StartsWith("{"))
				return ParseObject(source);

			if (ts.StartsWith("["))
				return ParseList(source);

			var tsLower = ts.ToLower();
			if (tsLower == "true")
				return true;
			if (tsLower == "false")
				return false;

			if (source.Contains('"') && quoteStrings)
			{
				var parts = ParseQuotedStrings(source);
				if (parts.Count == 0)
					return string.Empty;

				var result = parts[0];

				if (quoteDate)
				{
					DateTime dt;
					if (result.Contains('T') && result.Contains(':') && result.Contains('-') && DateTime.TryParse(result, out dt))
						return dt;
				}

				return result;
			}
			
			if (source.Contains('.'))
			{
				decimal result;
				if (decimal.TryParse(source, out result))
					return result;
			}
			else
			{
				if (source.Length <= 11)
				{
					int iresult;
					if (int.TryParse(source, out iresult))
						return iresult;
				}
				else
				{

					long lresult;
					if (long.TryParse(source, out lresult))
						return lresult;
				}

				if (!quoteDate)
				{
					DateTime dt;
					if (DateTime.TryParse(source, out dt))
						return dt;
				}
			}

			return trimStrings ? source.Trim() : source;
		}

		#endregion

		#region Xml Encoding

		public static string HtmlEncode(object source)
		{
			return XmlEncode(source);
		}

		public static string SqlEncode(object source)
		{
			if (source == null)
				return "NULL";

			if (source is string)
				return SqlEncode((string) source);

			if (source is DateTime)
				return string.Format("'{0:yyyy-MM-dd HH:mm:ss}'", (DateTime) source);
			
			if (source is bool)
				return ((bool) source) ? "1" : "0";

			if (source is decimal || source is TimeSpan)
				return string.Format("'{0}'", source);

			return source.ToString();
		}

		public static string SqlEncode(string source)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("'");
			foreach (var c in source)
			{
				if (c == '\'') sb.Append("''");
				else sb.Append(c);
			}
			sb.Append("'");
			return sb.ToString();
		}

		public static string XmlEncode(object source)
		{
			if (source == null) return "";

			if (!(source is string))
				return source.ToString();

			var txt = (string)source;
			StringBuilder sb = new StringBuilder(txt.Length);
			int inEscape = 0;
			foreach (var c in txt)
			{
				switch (inEscape)
				{
					#region Handle non escaped characters

					case 0:
						switch (c)
						{
							case '<':
								sb.Append("&lt;");
								break;
							case '>':
								sb.Append("&gt;");
								break;
							case '&':
								inEscape = 1;
								break;
							default:
								sb.Append(c);
								break;
						}
						break;

					#endregion

					#region Handle &
					case 1:
						switch (c)
						{
							case 'g':
								inEscape = 2;
								break;
							case 'l':
								inEscape = 4;
								break;
							case 'a':
								inEscape = 6;
								break;
							default:
								sb.Append("&amp;");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					#endregion

					#region Handle &gt;
					case 2:
						switch (c)
						{
							case 't':
								inEscape = 3;
								break;
							default:
								sb.Append("&amp;g");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					case 3:
						switch (c)
						{
							case ';':
								sb.Append("&gt;");
								inEscape = 0;
								break;
							default:
								sb.Append("&amp;gt");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					#endregion

					#region Handle &lt;

					case 4:
						switch (c)
						{
							case 't':
								inEscape = 5;
								break;
							default:
								sb.Append("&amp;l");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					case 5:
						switch (c)
						{
							case ';':
								sb.Append("&lt;");
								inEscape = 0;
								break;
							default:
								sb.Append("&amp;lt");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					#endregion

					#region Handle &amp;

					case 6:
						switch (c)
						{
							case 'm':
								inEscape = 7;
								break;
							default:
								sb.Append("&amp;a");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					case 7:
						switch (c)
						{
							case 'p':
								inEscape = 8;
								break;
							default:
								sb.Append("&amp;am");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;

					case 8:
						switch (c)
						{
							case ';':
								sb.Append("&amp;");
								inEscape = 0;
								break;
							default:
								sb.Append("&amp;amp");
								sb.Append(c);
								inEscape = 0;
								break;
						}
						break;
					#endregion
				}
			}
			return sb.ToString();
		}

		#endregion

		#region Date Formatting

		/// <summary>
		///  Returns ISO 8601 without trailing zeros on the milliseconds
		/// </summary>
		/// <param name="source"></param>
		/// <returns></returns>
		public static string TrimStandardDate(DateTime source)
		{
			var result = source.ToString("o");
			return result;
		}

		#endregion

		#region Delimited Rows
		public static Dictionary<int, StringBuilder> SplitDelimitedLine(string line, char delimiter = ',')
		{
			var quoted = false;
			var escaped = false;
			var parseColumn = false;
			var columnIndex = 0;
			var quoter = '"';

			var data = new Dictionary<int, StringBuilder>();
			
			foreach (var c in line)
			{
				if (!data.ContainsKey(columnIndex)) data[columnIndex] = new StringBuilder();
				var column = data[columnIndex];

				#region handle escaped characters
				// Todo: does not fully process escaped unicode characters
				if (escaped)
				{
					escaped = false;
					switch (char.ToLowerInvariant(c))
					{
						case 't': column.Append('\t'); break;
						case 'n': column.Append('\n'); break;
						case 'r': column.Append('\r'); break;
						case '0': column.Append('\0'); break;
						case 'a': column.Append('\a'); break;
						default: column.Append(c); break;
					}
					continue;
				}
				#endregion

				if (quoted)
				{
					#region handle quoted string
					if (c == quoter)
					{
						quoted = false;
						parseColumn = false;
						columnIndex++;
					}
					else if (c == '\\')
					{
						escaped = true;
					}
					else
					{
						column.Append(c);
					}
					#endregion
				}
				else
				{
					if (parseColumn)
					{
						#region handle parsing non-quoted column
						if (c == delimiter)
						{
							columnIndex++;
							parseColumn = false;
						}
						else
						{
							column.Append(c);
						}
						#endregion
					}
					else
					{
						#region handle parsing between columns
						if (c == '"' || c == '\'')
						{
							quoted = true;
							quoter = c;
							parseColumn = true;
						}
						else if (c == delimiter)
						{
							columnIndex++;
						}
						else if (!char.IsWhiteSpace(c))
						{
							parseColumn = true;
							column.Append(c);
						}
						#endregion
					}
				}
			}
			return data;
		}
		#endregion

		public static string ConvertToCamelCase(string source)
		{
			var sb = new StringBuilder();
			bool nextUpper = true;

			foreach (var c in source)
			{
				if (char.IsSeparator(c) || c == '_' || c == '-')
				{
					nextUpper = true;
					continue;
				}

				if (char.IsUpper(c))
				{
					nextUpper = false;
				}
				else if (nextUpper && char.IsLetter(c))
				{
					sb.Append(char.ToUpper(c));
					nextUpper = false;
					continue;
				}

				sb.Append(c);
			}

			return sb.ToString();
		}

		public static string ExpandCamelCase(string source)
		{
			var sb = new StringBuilder();
			bool inlower = false;
			
			foreach (var c in source)
			{
				if (char.IsUpper(c))
				{
					if (inlower) sb.Append(" ");

					inlower = false;
				}
				else
				{
					inlower = char.IsLower(c);
				}

				sb.Append(c);
			}

			return sb.ToString();
		}

		public static string BytesToString(long byteCount)
		{
			string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
			if (byteCount == 0)
				return "0" + suf[0];
			long bytes = Math.Abs(byteCount);
			int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
			double num = Math.Round(bytes / Math.Pow(1024, place), 1);
			return (Math.Sign(byteCount) * num).ToString() + suf[place];
		}

		public static string DecodeBase64(string encoded)
		{
			byte[] data = Convert.FromBase64String(encoded);
			return Encoding.UTF8.GetString(data);
		}

		public static string EncodeBase64(string decoded)
		{
			byte[] data = Encoding.UTF8.GetBytes(decoded);
			return Convert.ToBase64String(data);
		}

		public static string RemoveAccentedCharacters(string source)
		{
			bool accentedFound = source.Any(t => t > 127);
			if (!accentedFound) return source;

			Encoding encoding = Encoding.GetEncoding("iso-8859-8");
			byte[] buffer = encoding.GetBytes(source);
			return encoding.GetString(buffer);
		}


		public static string ConvertToBase(long decimalNumber, int radix, int width=0, bool trim = false, string prefix=null)
		{
			const int bitsInLong = 64;
			const string digits36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
			const string digits31 = "0123456789BCDFGHJKLMNPQRSTVWXYZ";
			const string digits22 = "ybdrfg8jkmcpqxtw34h769";

			var digits = radix == 22 ? digits22 : radix > 31 ? digits36 : (radix <= 16 ? digits36 : digits31);

			if (radix < 2 || radix > digits.Length)
				throw new ArgumentException("The radix must be >= 2 and <= " + digits.Length.ToString());

			if (decimalNumber == 0)
				return digits[0].ToString();

			int index = bitsInLong - 1;
			long currentNumber = Math.Abs(decimalNumber);
			char[] charArray = new char[bitsInLong];

			while (currentNumber != 0)
			{
				int remainder = (int)(currentNumber % radix);
				charArray[index--] = digits[remainder];
				currentNumber = currentNumber / radix;
			}

			string result = new String(charArray, index + 1, bitsInLong - index - 1);
			if (decimalNumber < 0)
			{
				result = "-" + result;
			}
			else if (width != 0)
			{
				if (result.Length < width)
					result = result.PadLeft(width, '0');
				else if (trim && result.Length > width)
					result = result.Substring(result.Length - width);
			}

			if (prefix != null)
				return prefix + result;

			return result;
		}

		public static long ConvertFromBase(string number, int radix)
		{
			const string digits36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
			const string digits31 = "0123456789BCDFGHJKLMNPQRSTVWXYZ";
			const string digits22 = "ybdrfg8jkmcpqxtw34h769";
			 
			var digits = radix == 22 ? digits22 : radix > 31 ? digits36 : (radix <= 16 ? digits36 : digits31);

			if (radix < 2 || radix > digits.Length)
				throw new ArgumentException("The radix must be >= 2 and <= " +
											digits.Length.ToString());

			if (String.IsNullOrEmpty(number))
				return 0;

			// Make sure the arbitrary numeral system number is in upper case
			number = number.ToUpperInvariant();

			long result = 0;
			long multiplier = 1;
			for (int i = number.Length - 1; i >= 0; i--)
			{
				char c = number[i];
				if (i == 0 && c == '-')
				{
					// This is the negative sign symbol
					result = -result;
					break;
				}

				int digit = digits.IndexOf(c);
				if (digit == -1)
					throw new ArgumentException(
						String.Format("Invalid character in the number: '{0}'", number),
						"number");

				result += digit*multiplier;
				multiplier *= radix;
			}

			return result;
		}

		public static string NewGuidString()
		{
			return GuidToString(Guid.NewGuid());
		}

		public static string GetZBaseTimeString(DateTime? date = null, bool useTicks = false)
		{
			var d = date.HasValue ? date.GetValueOrDefault() : DateTime.Now;
			byte[] bytes;

			if (!useTicks)
			{
				var dayOne = new DateTime(d.Year, 1, 1);
				var ts = d - dayOne;
				var seconds = Convert.ToInt32(ts.TotalSeconds);
				bytes = BitConverter.GetBytes(seconds).Reverse().ToArray();
				var result = ToZBase32String(bytes);

				// We actually don't need the first character since its always a 'y' for all seconds of the year
				return result.Substring(1);
			}

			bytes = BitConverter.GetBytes(d.Ticks).Reverse().ToArray();
			return ToZBase32String(bytes);
		
		}

		public static string GuidToString(Guid guid)
		{
			return ToZBase32String(guid.ToByteArray());	
		}

		public static Guid StringToGuid(string input)
		{
			return new Guid(FromZBase32String(input));
		}


		public static byte[] FromZBase32String(string input)
		{
			return Base32Encoder.FromBase32String(input, Base32Encoder.ZBase32Alphabet);
		}

		public static string ToZBase32String(byte[] input)
		{
			return Base32Encoder.ToBase32String(input, Base32Encoder.ZBase32Alphabet);
		}

		public static byte[] FromBase32String(string input)
		{
			return Base32Encoder.FromBase32String(input, Base32Encoder.ZBase32Alphabet);
		}

		public static string ToBase32String(byte[] input)
		{
			return Base32Encoder.ToBase32String(input, Base32Encoder.ZBase32Alphabet);
		}
	}


	internal class Base32Encoder
	{
		public const char StandardPaddingChar = '=';
		public const string Base32StandardAlphabet	= "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
		public const string ZBase32Alphabet			= "ybndrfg8ejkmcpqxot1uwisza345h769";

		public char PaddingChar;
		public bool UsePadding;
		public bool IsCaseSensitive;
		public bool IgnoreWhiteSpaceWhenDecoding;
		private readonly string _alphabet;
		private Dictionary<string, uint> _index;
		// alphabets may be used with varying case sensitivity, thus index must not ignore case
		private static Dictionary<string, Dictionary<string, uint>> _indexes = new Dictionary<string, Dictionary<string, uint>>(2, StringComparer.InvariantCulture);
		/// <summary>
		/// Create case insensitive encoder/decoder using the standard base32 alphabet without padding.
		/// White space is not permitted when decoding (not ignored).
		/// </summary>
		public Base32Encoder() : this(false, false, false, Base32StandardAlphabet) { }
		/// <summary>
		/// Create case insensitive encoder/decoder using the standard base32 alphabet.
		/// White space is not permitted when decoding (not ignored).
		/// </summary>
		/// <param name="padding">Require/use padding characters?</param>
		public Base32Encoder(bool padding) : this(padding, false, false, Base32StandardAlphabet) { }
		/// <summary>
		/// Create encoder/decoder using the standard base32 alphabet.
		/// White space is not permitted when decoding (not ignored).
		/// </summary>
		/// <param name="padding">Require/use padding characters?</param>
		/// <param name="caseSensitive">Be case sensitive when decoding?</param>
		public Base32Encoder(bool padding, bool caseSensitive) : this(padding, caseSensitive, false, Base32StandardAlphabet) { }
		/// <summary>
		/// Create encoder/decoder using the standard base32 alphabet.
		/// </summary>
		/// <param name="padding">Require/use padding characters?</param>
		/// <param name="caseSensitive">Be case sensitive when decoding?</param>
		/// <param name="ignoreWhiteSpaceWhenDecoding">Ignore / allow white space when decoding?</param>
		public Base32Encoder(bool padding, bool caseSensitive, bool ignoreWhiteSpaceWhenDecoding) : this(padding, caseSensitive, ignoreWhiteSpaceWhenDecoding, Base32StandardAlphabet) { }
		/// <summary>
		/// Create case insensitive encoder/decoder with alternative alphabet and no padding.
		/// White space is not permitted when decoding (not ignored).
		/// </summary>
		/// <param name="alternateAlphabet">Alphabet to use (such as Base32Url.ZBase32Alphabet)</param>
		public Base32Encoder(string alternateAlphabet) : this(false, false, false, alternateAlphabet) { }
		/// <summary>
		/// Create the encoder/decoder specifying all options manually.
		/// </summary>
		/// <param name="padding">Require/use padding characters?</param>
		/// <param name="caseSensitive">Be case sensitive when decoding?</param>
		/// <param name="ignoreWhiteSpaceWhenDecoding">Ignore / allow white space when decoding?</param>
		/// <param name="alternateAlphabet">Alphabet to use (such as Base32Url.ZBase32Alphabet, Base32Url.Base32StandardAlphabet or your own custom 32 character alphabet string)</param>
		public Base32Encoder(bool padding, bool caseSensitive, bool ignoreWhiteSpaceWhenDecoding, string alternateAlphabet)
		{
			if (alternateAlphabet.Length != 32)
			{
				throw new ArgumentException("Alphabet must be exactly 32 characters long for base 32 encoding.");
			}
			PaddingChar = StandardPaddingChar;
			UsePadding = padding;
			IsCaseSensitive = caseSensitive;
			IgnoreWhiteSpaceWhenDecoding = ignoreWhiteSpaceWhenDecoding;
			_alphabet = alternateAlphabet;
		}
		/// <summary>
		/// Decode a base32 string to a byte[] using the default options
		/// (case insensitive without padding using the standard base32 alphabet from rfc4648).
		/// White space is not permitted (not ignored).
		/// Use alternative constructors for more options.
		/// </summary>
		public static byte[] FromBase32String(string input, string alternateAlphabet=null)
		{
			return alternateAlphabet != null ? new Base32Encoder(alternateAlphabet).Decode(input) : new Base32Encoder().Decode(input);
		}

		/// <summary>
		/// Encode a base32 string from a byte[] using the default options
		/// (case insensitive without padding using the standard base32 alphabet from rfc4648).
		/// Use alternative constructors for more options.
		/// </summary>
		public static string ToBase32String(byte[] data, string alternateAlphabet=null)
		{
			return alternateAlphabet != null ? new Base32Encoder(alternateAlphabet).Encode(data) : new Base32Encoder().Encode(data);
		}
		public string Encode(byte[] data)
		{
			StringBuilder result = new StringBuilder(Math.Max((int)Math.Ceiling(data.Length * 8 / 5.0), 1));
			byte[] emptyBuff = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
			byte[] buff = new byte[8];
			// take input five bytes at a time to chunk it up for encoding
			for (int i = 0; i < data.Length; i += 5)
			{
				int bytes = Math.Min(data.Length - i, 5);
				// parse five bytes at a time using an 8 byte ulong
				Array.Copy(emptyBuff, buff, emptyBuff.Length);
				Array.Copy(data, i, buff, buff.Length - (bytes + 1), bytes);
				Array.Reverse(buff);
				ulong val = BitConverter.ToUInt64(buff, 0);
				for (int bitOffset = ((bytes + 1) * 8) - 5; bitOffset > 3; bitOffset -= 5)
				{
					result.Append(_alphabet[(int)((val >> bitOffset) & 0x1f)]);
				}
			}
			if (UsePadding)
			{
				result.Append(string.Empty.PadRight((result.Length % 8) == 0 ? 0 : (8 - (result.Length % 8)), PaddingChar));
			}
			return result.ToString();
		}
		public byte[] Decode(string input)
		{
			if (IgnoreWhiteSpaceWhenDecoding)
			{
				input = Regex.Replace(input, "\\s+", "");
			}
			if (UsePadding)
			{
				if (input.Length % 8 != 0)
				{
					throw new ArgumentException("Invalid length for a base32 string with padding.");
				}
				input = input.TrimEnd(PaddingChar);
			}
			// index the alphabet for decoding only when needed
			EnsureAlphabetIndexed();
			MemoryStream ms = new MemoryStream(Math.Max((int)Math.Ceiling(input.Length * 5 / 8.0), 1));
			// take input eight bytes at a time to chunk it up for encoding
			for (int i = 0; i < input.Length; i += 8)
			{
				int chars = Math.Min(input.Length - i, 8);
				ulong val = 0;
				int bytes = (int)Math.Floor(chars * (5 / 8.0));
				for (int charOffset = 0; charOffset < chars; charOffset++)
				{
					uint cbyte;
					if (!_index.TryGetValue(input.Substring(i + charOffset, 1), out cbyte))
					{
						throw new ArgumentException("Invalid character '" + input.Substring(i + charOffset, 1) + "' in base32 string, valid characters are: " + _alphabet);
					}
					val |= (((ulong)cbyte) << ((((bytes + 1) * 8) - (charOffset * 5)) - 5));
				}
				byte[] buff = BitConverter.GetBytes(val);
				Array.Reverse(buff);
				ms.Write(buff, buff.Length - (bytes + 1), bytes);
			}
			return ms.ToArray();
		}
		private void EnsureAlphabetIndexed()
		{
			if (_index == null)
			{
				Dictionary<string, uint> cidx;
				string indexKey = (IsCaseSensitive ? "S" : "I") + _alphabet;
				if (!_indexes.TryGetValue(indexKey, out cidx))
				{
					lock (_indexes)
					{
						if (!_indexes.TryGetValue(indexKey, out cidx))
						{
							cidx = new Dictionary<string, uint>(_alphabet.Length, IsCaseSensitive ? StringComparer.InvariantCulture : StringComparer.InvariantCultureIgnoreCase);
							for (int i = 0; i < _alphabet.Length; i++)
							{
								cidx[_alphabet.Substring(i, 1)] = (uint)i;
							}
							_indexes.Add(indexKey, cidx);
						}
					}
				}
				_index = cidx;
			}
		}
	}
}
