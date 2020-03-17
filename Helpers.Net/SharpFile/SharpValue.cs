using Helpers.Net.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Helpers.Net.Objects
{
	internal class SharpValue
	{
		public SharpValueType Type = SharpValueType.None;
		public object Value;
		public string Format;

		public SharpValue() {}

		public SharpValue(SharpValue source)
		{
			Type = source.Type;
			Value = source.Value;
			Format = source.Format;
		}

		public SharpValue(string value, bool parseQuoted = false)
		{
			Type = SharpValueType.String;
			Value = parseQuoted ? EncodedString.ParseQuotedString(value) : value;
		}

		public SharpValue(object value)
		{
			Value = value;

			if (value is string)
				Type = SharpValueType.String;
			else if (value is int)
				Type = SharpValueType.Int;
			else if (value is bool)
				Type = SharpValueType.Bool;
			else if (value is DateTime)
				Type = SharpValueType.DateTime;
			else if (value is TimeSpan)
				Type = SharpValueType.TimeSpan;
			else if (value is long)
				Type = SharpValueType.Long;
			else if (value is float || value is double)
				Type = SharpValueType.Double;
			else if (value is decimal)
				Type = SharpValueType.Decimal;
			else if (value is List<int> || value is int[])
				Type = SharpValueType.List;
			else if (value is List<string> || value is string[])
				Type = SharpValueType.List;
		}

		public static SharpValueType GetValueType(object value)
		{
			if (value is string)
				return SharpValueType.String;
			if (value is int)
				return SharpValueType.Int;
			if (value is bool)
				return SharpValueType.Bool;
			if (value is DateTime)
				return SharpValueType.DateTime;
			if (value is TimeSpan)
				return SharpValueType.TimeSpan;
			if (value is long)
				return SharpValueType.Long;
			if (value is float || value is double)
				return SharpValueType.Double;
			if (value is decimal)
				return SharpValueType.Decimal;
			if (value is List<int> || value is int[])
				return SharpValueType.List;
			if (value is List<string> || value is string[])
				return SharpValueType.List;

			return SharpValueType.None;
		}

		public static object AsValueList(IEnumerable<object> values, SharpValueType type)
		{
			try
			{
				switch (type)
				{
					case SharpValueType.Bool:
						return values.Cast<bool>();
					case SharpValueType.String:
						return values.Cast<string>();
					case SharpValueType.Int:
						return values.Cast<int>();
					case SharpValueType.Long:
						return values.Cast<long>();
					case SharpValueType.Decimal:
						return values.Cast<Decimal>();
					case SharpValueType.Double:
						return values.Cast<double>();
					case SharpValueType.Date:
					case SharpValueType.DateTime:
					case SharpValueType.TimeStamp:
						return values.Cast<DateTime>();
					case SharpValueType.TimeSpan:
						return values.Cast<TimeSpan>();
				}
			}
			catch
			{
			}

			return values;
		}

		public static bool TryGetValueList(object value, out SharpValueType type)
		{
			type = SharpValueType.None;

			if (value is string)
				return false;

			if (value is IEnumerable<int>)
			{
				type = SharpValueType.Int;
				return true;
			}

			if (value is IEnumerable<string>)
			{
				type = SharpValueType.String;
				return true;
			}

			if (value is IEnumerable<bool>)
			{
				type = SharpValueType.Bool;
				return true;
			}

			if (value is IEnumerable<DateTime>)
			{
				type = SharpValueType.DateTime;
				return true;
			}

			if (value is IEnumerable<double>)
			{
				type = SharpValueType.Double;
				return true;
			}

			if (value is IEnumerable<decimal>)
			{
				type = SharpValueType.Decimal;
				return true;
			}

			if (value is IEnumerable<long>)
			{
				type = SharpValueType.Long;
				return true;
			}

			return value is IEnumerable;
		}


		public static string EncodeValue(object value, SharpValueType valueType, char delimiter = '\0', bool quoteQuotes = false)
		{
			if (value == null)
				return "";

			if (value is DateTime)
				return EncodedString.TrimStandardDate((DateTime)value);

			if (!(value is string))
				return value.ToString();

			var result = value.ToString();

			if (EncodedString.IsQuotedStringEncodingRequired(result, delimiter: delimiter, quoteQuotes: quoteQuotes))
				return EncodedString.EncodeQuotedString(result);
			
			return result;
		}

		public override string ToString()
		{
			return Value.ToString();
		}

		public static string ToString(object value, SharpValueType type)
		{
			if (value == null)
				return string.Empty;

			return value.ToString();
		}

		public static object ToValue(string value, SharpValueType type, bool autoType = false)
		{
			if (value == null)
				return null;

			switch (type)
			{
				case SharpValueType.None:
					if (autoType) return ToValue(value, SharpValueType.Auto);
					return value;
				case SharpValueType.String:
					return value;
				case SharpValueType.Bool:
					{
						bool boolResult;
						if (bool.TryParse(value, out boolResult))
							return boolResult;

						return value;
					}
				case SharpValueType.Int:
					{
						int intResult;
						if (int.TryParse(value, out intResult))
							return intResult;

						return value;
					}
				case SharpValueType.Long:
					{
						long longResult;
						if (long.TryParse(value, out longResult))
							return longResult;

						return value;
					}
				case SharpValueType.Date:
				case SharpValueType.DateTime:
				case SharpValueType.TimeStamp:
					{
						DateTime dateResult;
						if (DateTime.TryParse(value, out dateResult))
							return dateResult;

						return value;
					}
				case SharpValueType.TimeSpan:
					{
						TimeSpan timeSpanResult;
						if (TimeSpan.TryParse(value, out timeSpanResult))
							return timeSpanResult;
						return value;
					}
				case SharpValueType.Double:
					{
						double doubleResult;
						if (double.TryParse(value, out doubleResult))
							return doubleResult;
						return value;
					}
				case SharpValueType.Decimal:
					{
						Decimal decimalResult;
						if (Decimal.TryParse(value, out decimalResult))
							return decimalResult;
						return value;
					}

				case SharpValueType.NumericString:
					{
						if (NumericString.CanEncode(value))
							return new NumericString(value);
						return value;
					}

				case SharpValueType.List:
					{
						return EncodedString.ParseList(value, trimStrings:true);
					}

				case SharpValueType.Object:
					{
						return EncodedString.ParseObject(value);
					}
			}

			return value;
		}
		
		public static Type GetFieldType(SharpValueType type)
		{
			switch (type)
			{
				case SharpValueType.None:
					return typeof (string);
				case SharpValueType.Bool:
					return typeof (bool);
				case SharpValueType.Int:
					return typeof (int);
				case SharpValueType.Long:
					return typeof (long);
				case SharpValueType.Double:
					return typeof (double);
				case SharpValueType.Decimal:
					return typeof (Decimal);
				case SharpValueType.Date:
				case SharpValueType.DateTime:
				case SharpValueType.TimeStamp:
					return typeof (DateTime);
				case SharpValueType.TimeSpan:
					return typeof (TimeSpan);
				case SharpValueType.List:
					return typeof (IList<object>);
				case SharpValueType.Object:
					return typeof (object);
				default:
					return typeof (string);
			}
		}

		public static bool IsPrimitiveType(SharpValueType type)
		{
			switch (type)
			{
				case SharpValueType.String:
				case SharpValueType.None:
				case SharpValueType.Bool:
				case SharpValueType.Long:
				case SharpValueType.Double:
				case SharpValueType.Int: 
					return true;

				case SharpValueType.Date:
				case SharpValueType.DateTime:
				case SharpValueType.TimeStamp:
				case SharpValueType.TimeSpan:
				case SharpValueType.List:
				case SharpValueType.Decimal:
				case SharpValueType.Object:
					return false;

			}
			return false;
		}

		public static object GetDefaultValue(SharpValueType type)
		{
			switch (type)
			{
				case SharpValueType.String:
				case SharpValueType.NumericString:
				case SharpValueType.None:
					return string.Empty;
				case SharpValueType.Bool:
					return default(bool);
				case SharpValueType.Int:
					return default(int);
				case SharpValueType.Long:
					return default(long);
				case SharpValueType.Double:
					return default(double);
				case SharpValueType.Decimal:
					return Decimal.Zero;
				case SharpValueType.Date:
				case SharpValueType.DateTime:
				case SharpValueType.TimeStamp:
					return DateTime.MinValue;
				case SharpValueType.TimeSpan:
					return TimeSpan.Zero;
				case SharpValueType.List:
					return new List<object>();
				case SharpValueType.Object:
					return new SharpObject();
				default:
					return null;
			}
		}
	}
}
