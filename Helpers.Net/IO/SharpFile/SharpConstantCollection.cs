using System.Collections.Generic;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpConstantCollection
	{
		private int _nextIndex = 0;
		private bool _internStrings = true;
		private bool _indexOnInsert = false;

		private readonly Dictionary<string, string> _intern = new Dictionary<string, string>();

		private readonly Dictionary<object, int> _index = new Dictionary<object, int>();
		private readonly Dictionary<int, object> _cache = new Dictionary<int, object>();

		public void Insert(int valueIndex, object value)
		{
			if (_internStrings && value is string)
			{
				string svalue = (string)value;

				if (!_intern.ContainsKey(svalue))
					_intern[svalue] = svalue;
				else
					value = _intern[svalue];
			}

			_cache[valueIndex] = value;

			if (_indexOnInsert)
				_index[value] = valueIndex;

			if (_nextIndex <= valueIndex)
				_nextIndex = valueIndex + 1;
		}

		public int Append(object value)
		{
			int index;
			if (_index.TryGetValue(value, out index))
				return index;

			index = _nextIndex++;

			if (_internStrings && value is string)
			{
				string svalue = (string)value;

				if (!_intern.ContainsKey(svalue))
					_intern[svalue] = svalue;
				else
					value = _intern[svalue];
			}

			_index[value] = index;
			_cache[index] = value;

			return index;
		}

		public T GetValue<T>(int valueIndex)
		{
			object result;
			if (_cache.TryGetValue(valueIndex, out result))
				return (T)result;

			return default(T);
		}

		public T GetValue<T>(int valueIndex, T defaultValue)
		{
			object result;
			if (_cache.TryGetValue(valueIndex, out result))
				return (T)result;

			return defaultValue;
		}

		public object GetValue(int valueIndex, object defaultValue = null)
		{
			object result;
			if (_cache.TryGetValue(valueIndex, out result))
				return result;

			return defaultValue;
		}

		public bool Contains(int valueIndex)
		{
			return _cache.ContainsKey(valueIndex);
		}

		public string Intern(string value)
		{
			if (!_intern.ContainsKey(value))
				_intern[value] = value;
			else
				value = _intern[value];
			return value;
		}

		public void Clear()
		{
			_intern.Clear();
			_index.Clear();
			_cache.Clear();
		}

		public void ClearIntern()
		{
			_intern.Clear();
		}

		public void ClearIndex()
		{
			_index.Clear();
		}

		public void RefreshIndex()
		{
			_index.Clear();
			foreach (var item in _cache)
			{
				_index[item.Value] = item.Key;
			}
		}

	}
}
