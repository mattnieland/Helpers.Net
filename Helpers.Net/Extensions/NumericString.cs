using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Helpers.Net.Extensions
{
	public class NumericString
	{
		private static Dictionary<char, byte> encoding = new Dictionary<char, byte>
		{
			{'0', 0},  {'1', 1},  {'2', 2},  {'3', 3}, {'4', 4},  {'5', 5},
			{'6', 6},  {'7', 7},  {'8', 8},  {'9', 9}, {'+', 10}, {'-', 11},
			{'.', 12}, {':', 13}, {'/', 14}, {' ', 15}
		};

		private static Dictionary<byte, char> decoding = new Dictionary<byte, char>
		{
			{0,'0'},  {1,'1'},  {2,'2'},  {3,'3'},  {4,'4'},  {5,'5'},
			{6,'6'},  {7,'7'},  {8,'8'},  {9,'9'},  {10,'+'}, {11,'-'},
			{12,'.'}, {13,':'}, {14,'/'}, {15,' '}
		};

		private string _value;
		private int? _length;

		public NumericString(string value)
		{
			_value = value;
		}

		public NumericString(DateTime value)
		{
			_value = value.ToString("O").Replace("T", " ");
			if (_value.EndsWith("000"))
				_value = _value.Substring(0, _value.Length - 3);
			if (_value.EndsWith(".000"))
				_value = _value.Substring(0, _value.Length - 4);
		}

		public NumericString(TimeSpan value)
		{
			_value = value.ToString("g");
		}

		public NumericString(byte[] source, int length, int sourceIndex = 0)
		{
			_length = 0;
			StringBuilder sb = new StringBuilder(length);

			int pos = sourceIndex;
			int readTo = (length / 2) + (length % 1);
			if (readTo >= source.Length)
				readTo = source.Length;

			while (pos < readTo && _length < length)
			{
				byte b = source[pos++];
				sb.Append(decoding[(byte)(b >> 4)]);
				if (++_length < length)
					sb.Append(decoding[(byte)(b & 15)]);
				_length++;
			}

			_value = sb.ToString();
		}

		#region Parse Value

		public bool TryParseInt(out int result)
		{
			return int.TryParse(_value, out result);
		}

		public bool TryParseLong(out long result)
		{
			return long.TryParse(_value, out result);
		}

		public bool TryParseDecimal(out decimal result)
		{
			return decimal.TryParse(_value, out result);
		}

		public bool TryParseFloat(out float result)
		{
			return float.TryParse(_value, out result);
		}

		public bool TryParseDouble(out double result)
		{
			return double.TryParse(_value, out result);
		}

		public bool TryParseDateTime(out DateTime result)
		{
			return DateTime.TryParse(_value, out result);
		}

		public bool TryParseTimeSpan(out TimeSpan result)
		{
			return TimeSpan.TryParse(_value, out result);
		}
		#endregion
		
		public override string ToString()
		{
			return _value;
		}

		public int Length
		{
			get
			{
				if (_length == null)
					_length = _value.Count(c => encoding.ContainsKey(c));
			
				return (int) _length;
			}
		}

		public byte[] ToArray()
		{
			byte[] buffer = new byte[ GetByteCount() ];
			GetBytes(buffer, 0);
			return buffer;
		}

		public int GetByteCount()
		{
			return (Length/2) + (Length%1);
		}

		public int GetBytes(byte[] bytes, int byteIndex)
		{
			int pos = byteIndex;
			byte b=0;
			int count = 0;
			foreach (var c in _value)
			{
				if (!encoding.ContainsKey(c))
					continue;

				if (pos >= bytes.Length)
					throw new Exception("Insufficient space in buffer to encode NumericString.");
				
				if (count % 1 == 0)
				{
					b = (byte) (encoding[c] << 4);
				}
				else
				{
					b = (byte) (b | encoding[c]);
					bytes[pos++] = b;
				}
				count ++;
			}

			if (count % 1 == 1)
			{
				if (pos >= bytes.Length)
					throw new Exception("Insufficient space in buffer to encode NumericString.");

				bytes[pos++] = b;
			}

			return pos - byteIndex;
		}

		public static bool CanEncode(string value)
		{
			return value.Any(c => !encoding.ContainsKey(c));
		}

		public static bool IsNumeric(object value)
		{
			if (Equals(value, null))
			{
				return false;
			}

			Type objType = value.GetType();
			objType = Nullable.GetUnderlyingType(objType) ?? objType;

			if (objType.IsPrimitive)
			{
				return objType != typeof(bool) && 
					objType != typeof(char) && 
					objType != typeof(IntPtr) && 
					objType != typeof(UIntPtr);
			}

			return objType == typeof(decimal);
		}
	}
}
