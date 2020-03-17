using Helpers.Net.Extensions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Helpers.Net.Objects
{
	public interface ISharpObject
	{
		SharpObject GetObject();
	}

	public interface ISharpObjectFormat
	{
		bool ContainsField(string key);
		object GetFieldValue(string key);
	}

	public interface ISharpObjectStream : IDisposable
	{
		IEnumerable<SharpObject> Select();
		void Write(SharpObject source);
		void Write(IEnumerable<SharpObject> source);
		void Close();
	}

	public class SharpObject : ISharpObject, ISharpObjectFormat, IDictionary<string, object>
	{
		private static char[] _pathSep = new[] {'/'};
		private static Regex _arrayIndex = new Regex(@"([^[]+)\[(.*)\]");

		private readonly object _sync = new object();
		private bool _isExpanded = false;
		
		private bool _autoType = true;

		private readonly Dictionary<string, SharpNodeRow> _rows = new Dictionary<string, SharpNodeRow>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, int> _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
 
		private readonly Dictionary<string, object> _members = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, SharpValueType> _types = new Dictionary<string, SharpValueType>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, object> _meta = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

		private SharpNodeRow _source;
		private int _columnId;

		private SharpObjectDynamic _wrapper;

		public SharpObject()
		{
			_isExpanded = true;
		
		}

		public SharpObject(DataSet dataSet)
		{
			_isExpanded = true;
		
			foreach (DataTable table in dataSet.Tables)
			{
				var rows = new List<SharpObject>();
				foreach(DataRow row in table.Rows)
					rows.Add(new SharpObject(row));
				this[table.TableName] = rows;
			}

		}

		public SharpObject(DataRow row)
		{
			_isExpanded = true;

			foreach (DataColumn col in row.Table.Columns)
			{
				this[col.ColumnName] = row.Field<object>(col);
			}

			
		}

		public SharpObject(DataRow row, string root)
		{
			_isExpanded = true;

			var nodes = new Dictionary<string, SharpObject>();
			var sep = new [] {'_'};

			foreach (DataColumn col in row.Table.Columns)
			{
				var parts = col.ColumnName.Split(sep, 2);

				var nodeName = parts.Length > 1 ? parts[0] : root;
				var fieldName = parts.Length > 1 ? parts[1] : parts[0];

				SharpObject obj;
				if (nodeName == root)
				{
					obj = this;
				}
				else
				{
				   
					if (!nodes.TryGetValue(nodeName, out obj))
					{
						obj = new SharpObject();
						nodes[nodeName] = obj;
						Add(nodeName, obj);
					}
				}

				obj[fieldName] = row.Field<object>(col);
			}

		}

		public SharpObject(JObject source)
		{
			_isExpanded = true;
			foreach (var col in source.ToDictionary())
			{
				this[col.Key] = col.Value;
			}
		}

		public SharpObject(IEnumerable<KeyValuePair<string, object>> source)
		{
			foreach (var item in source)
			{
				if (ContainsKey(item.Key))
					Add(item.Key, item.Value);
				else
					this[item.Key] = item.Value;
			}
		}

		public static SharpObject Copy<T>(IDictionary<string, object> source)
		{
			return new SharpObject(source.ToDictionary(x => x.Key, x => (object) x.Key));
		}
		
		public SharpObject(IDictionary<string, object> source)
		{
			_isExpanded = true;
			foreach (var item in source)
			{
				this[item.Key] = item.Value;
			}
		}

		internal SharpObject(SharpNodeRow source)
		{
			_source = source;
		}

		internal SharpObject(SharpNodeRow source, object value, SharpValueType type)
		{
			_source = source;
			_isExpanded = true;

			// '#' is placeholder for SharpObject's own value. 
			_members["#"] = value;
			_types["#"] = type;
		}

		public void Unlink()
		{
			ExpandObject();
			foreach (var item in _collapsed.ToList())
			{
				ExpandMemberValue(item);
			}

			_source = null;
		}

		#region Merge Interface

		public static SharpObject Sort(SharpObject source, params string[] fieldOrder)
		{
			return Sort(source, fieldOrder.AsEnumerable());
		}

		public static SharpObject Sort(SharpObject source, IEnumerable<string> fieldOrder)
		{
			HashSet<string> added = new HashSet<string>();

			var result = new SharpObject();

			foreach (var key in fieldOrder.Where(source.ContainsKey))
			{
				result[key] = source[key];
				added.Add(key);
			}

			foreach (var key in source.Keys.Where(x => !added.Contains(x)))
			{
				result[key] = source[key];
			}

			return result;
		}

		public void MergeWith(Dictionary<string, object> source)
		{
			foreach (var item in source)
			{
				this[item.Key] = item.Value;
			}
		}

		public void MergeWith(Dictionary<string, string> source)
		{
			foreach (var item in source)
			{
				var arg = item.Value;

				if (arg.Length > 0 && arg[0] == '"')
					this[item.Key] = EncodedString.ParseQuotedString(arg);
				else
				{
					this[item.Key] = EncodedString.Parse(arg);
				}
			}
		}

		public void MergeWith(IEnumerable<string> values)
		{
			
			string argField = null;
			foreach (var arg in values)
			{
				if (argField == null)
				{
					argField = arg;
				}
				else
				{
					if (arg.Length > 0 && arg[0] == '"')
						this[argField] = EncodedString.ParseQuotedString(arg);
					else
					{
						this[argField] = EncodedString.Parse(arg);
					}

					argField = null;
				}
			}	
		}

		public void UpdateFrom(ISharpObject source)
		{
			CheckExpand();

			var obj = source.GetObject();

			foreach (var fieldName in obj.Keys)
			{
				if (!ContainsKey(fieldName))
					continue;

				var val = ExpandMemberValue(fieldName);
				var sourceVal = obj.ExpandMemberValue(fieldName);

				if (val == null)
				{
					this[fieldName] = sourceVal;
					continue;
				}

				if (val is SharpObject)
				{
					if (sourceVal is SharpObject)
					{
						((SharpObject)val).UpdateFrom((SharpObject)sourceVal);
					}
					else if (sourceVal is SharpObjectList)
					{

						var newVal = new List<SharpObject>() { (SharpObject)val };
						newVal.AddRange((SharpObjectList)sourceVal);

						this[fieldName] = new SharpObjectList(this, fieldName, newVal);
					}
					else
					{
						((SharpObject)val)["#"] = sourceVal;
					}
				}
				else if (val is SharpObjectList)
				{
					if (sourceVal is SharpObject)
					{
						((SharpObjectList)val).Add(((SharpObject)sourceVal));
					}
					else if (sourceVal is SharpObjectList)
					{
						((SharpObjectList)val).AddRange(((SharpObjectList)sourceVal));
					}
					else if (sourceVal is IEnumerable<object>)
					{
						foreach (var v in (IEnumerable<object>) sourceVal)
						{
							var sourceObj = new SharpObject();
							sourceObj["#"] = v;
							((SharpObjectList)val).Add(sourceObj);
						}
					}
					else
					{
						var sourceObj = new SharpObject();
						sourceObj["#"] = sourceVal;
						((SharpObjectList)val).Add(sourceObj);
					}
				}
				else
				{
					this[fieldName] = sourceVal;
				}
			}
		}

		public void CopyFrom(ISharpObject source)
		{
			CheckExpand();
			var obj = source.GetObject();

			foreach (var item in obj.EnumerateMemberValue())
				this[item.Key] = item.Value;
			foreach (var item in obj.EnumerateMemberObject())
				this[item.Key] = item.Value;
			foreach (var item in obj.EnumerateMemberObjectList())
				this[item.Key] = item.Value;
		}

		public void MergeWith(ISharpObject source)
		{
			CheckExpand();

			var obj = source.GetObject();

			foreach (var fieldName in obj.Keys)
			{
				var val = ExpandMemberValue(fieldName);
				var sourceVal = obj.ExpandMemberValue(fieldName);

				if (val == null)
				{
					this[fieldName] = sourceVal;
					continue;
				}

				if (val is SharpObject)
				{
					if (sourceVal is SharpObject)
					{
						((SharpObject) val).MergeWith((SharpObject) sourceVal);
					}
					else if (sourceVal is SharpObjectList)
					{

						var newVal = new List<SharpObject>() {(SharpObject) val};
						newVal.AddRange((SharpObjectList)sourceVal);

						this[fieldName] = new SharpObjectList(this, fieldName, newVal);
					}
					else
					{
						((SharpObject) val)["#"] = sourceVal;
					}
				}
				else if (val is SharpObjectList)
				{
					if (sourceVal is SharpObject)
					{
						((SharpObjectList) val).Add(((SharpObject) sourceVal));
					}
					else if (sourceVal is SharpObjectList)
					{
						((SharpObjectList) val).AddRange(((SharpObjectList) sourceVal));
					}
					else
					{
						var sourceObj = new SharpObject();
						sourceObj["#"] = sourceVal;
						((SharpObjectList) val).Add(sourceObj);
					}
				}
				else
				{
					this[fieldName] = sourceVal;
				}
			}
		}

		#endregion

		#region Generic Read Interface

		public bool IsNullOrEmpty(string path)
		{
			return string.IsNullOrEmpty(GetString(path));
		}

		public string GetString(string path, ISharpObjectFormat source, string defaultValue = null)
		{
			var result = GetMemberValue<string>(path);

			if (String.IsNullOrEmpty(result))
				result = defaultValue;

			return String.IsNullOrEmpty(result) ? result : SharpObject.Format(source, result);
		}

		public string GetString(string path, string defaultValue=null)
		{
			var result = GetMemberValue<string>(path);

			if (String.IsNullOrEmpty(result))
				result = defaultValue;
			
			return result;
		}

		public TimeSpan GetTimeSpan(string path, TimeSpan defaultValue = default(TimeSpan))
		{
			if (!ContainsKey(path))
				return defaultValue;

			return GetMemberValue<TimeSpan>(path);
		}

		public decimal GetDecimal(string path, decimal defaultValue = 0.0m)
		{
			return ContainsKey(path) ? GetMemberValue<decimal>(path) : defaultValue;
		}

		public double GetDouble(string path, double defaultValue = 0.0D)
		{
			return ContainsKey(path) ? GetMemberValue<double>(path) : defaultValue;
		}

		public DateTime GetDate(string path)
		{
			if (ContainsKey(path))
				return GetMemberValue<DateTime>(path);

			return DateTime.MinValue;
		}

		public DateTime GetDate(string path, DateTime defaultValue)
		{
			if (ContainsKey(path))
				return GetMemberValue<DateTime>(path);

			return defaultValue;
		}

		public int GetInt(string path, int defaultValue=0)
		{
			if (ContainsKey(path))
				return GetMemberValue<int>(path);
			
			return defaultValue;
		}

		public bool GetBool(string path, bool defaultValue=false)
		{
			if (ContainsKey(path))
				return GetMemberValue<bool>(path);

			return defaultValue;
		}

		public IEnumerable<KeyValuePair<string, List<string>>> GetMetaTypes()
		{
			foreach (var item in _meta.ToList())
			{
				if (!item.Key.StartsWith("~")) continue;

				var key = item.Key.Substring(1);
				var ls = item.Value as List<string>;
				if (ls != null)
					yield return new KeyValuePair<string, List<string>>(key, ls);
				else 
					yield return new KeyValuePair<string, List<string>>(key, new List<string> { item.Value.ToString()});
			}
		}

		public List<string> GetMetaTypeFields(string type)
		{
			var key = "~" + type.ToLower();
			object result;
			if (!_meta.TryGetValue(key, out result)) return new List<string>();

			var ls = result as List<string>;
			return ls ?? new List<string> {result.ToString()};
		}

		public void SetMetaType(string field, string type)
		{
			AppendMeta("~" + type.ToLower(), field, true);
		}

		public void AppendMeta(string key, string value, bool unique = false)
		{
			object result;
			if (_meta.TryGetValue(key, out result))
			{
				var ls = result as List<string>;
				if (ls != null)
				{
					if (!unique)
						ls.Add(value);
					else if (!ls.Contains(value))
						ls.Add(value);
				}
				else
				{
					var s = result.ToString();
					if (!unique || value != s)
						_meta[key] = new List<string> {s, value};
				}
			}
			else
			{
				_meta[key] = value;
			}
		}

		public bool ContainsMeta(string path)
		{
			return _meta.ContainsKey(path);
		}

		public object GetMeta(string path)
		{
			object result;
			return _meta.TryGetValue(path, out result) ? result : null;
		}

		public void SetMeta(string path, object value)
		{
			_meta[path] = value;
		}

		public SharpObjectList GetObjectList(string path)
		{
			CheckExpand();
			var name = GetMemberName(path);

			if (name != null)
			{
				var value = ExpandMemberValue(name);
				if (value is SharpObject)
				{
					return new SharpObjectList(this, "", new[] {value as SharpObject});
				}
				
				if (value is SharpObjectList)
				{
					return value as SharpObjectList;
				}

				return new SharpObjectList(this, "");
			}


			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.GetObjectList(nextPath);

			return new SharpObjectList(this, "");
		}

		public IList<string> GetStringList(string path)
		{
			return GetMemberValueList<string>(path);
		}

		public IList<int> GetIntList(string path)
		{
			return GetMemberValueList<int>(path);
		}

		public IList<bool> GetBoolList(string path)
		{
			return GetMemberValueList<bool>(path);
		}

		private T TryGetValue<T>(object value)
		{
			Type t = typeof(T);
			if (value != null && value is string)
			{
				if (t.Name == "Int32")
				{
					int vi;
					if (Int32.TryParse((string)value, out vi))
					{
						return (T)(object)vi;
					}
				}
				else if (t.Name == "String")
				{
					return (T)value;
				}
			}

			try
			{
				return (T) value;
			}
			catch (Exception)
			{
				throw new DataException(
						String.Format("No automatic conversion between {0} and {1}", value == null ? "<null>" : value.GetType().ToString(), t));
			}
		}

		public IList<T> GetMemberValueList<T>(string path)
		{
			CheckExpand();
			var name = GetMemberName(path);
			if (name != null)
			{
				var value = ExpandMemberValue(name);
				if (value is SharpObject)
					return new List<T> { (T)GetValue() };

				if (value is SharpObjectList)
				{
					var valueList = value as SharpObjectList;
					
					return valueList.Select(x => TryGetValue<T>(x.GetValue())).ToList();
				}

				if (value is List<object>)
				{
					return ((List<object>) value).Cast<T>().ToList();
				}
				
				return new List<T> { (T)ToValue(_types[name], value) };
			}


			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.GetMemberValueList<T>(nextPath);

			return new List<T>();
		}

		public string GetMemberType(string path)
		{
			CheckExpand();
			var name = GetMemberName(path);
			if (name != null)
			{
				if (_types.ContainsKey(name))
				{
					return _types[name].ToString();
				}

				return "None";
			}
			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.GetMemberType(nextPath);

			return "None";
		}
		public T GetMemberValue<T>(string path)
		{
			CheckExpand();
			var name = GetMemberName(path);
			if (name != null)
			{

				var value = ExpandMemberValue(name);
				if (value is SharpObject)
					return (T)GetValue();

				if (value is SharpObjectList)
				{
					var valueList = value as SharpObjectList;
					return valueList.Select(x => (T)x.GetValue()).FirstOrDefault();
				}

				if (value == null)
					return default(T);

				return (T)ToValue(_types[name], value);
			}

			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.GetMemberValue<T>(nextPath);

			return default(T);
		}

		public IEnumerable<T> GetMemberObjectEnumerable<T>(string path, Func<SharpObject, T> initFunc)
		{
			CheckExpand();
			var name = GetMemberName(path);
			if (name != null)
			{

				var value = ExpandMemberValue(name);
				if (value is SharpObject)
					return new List<T> { initFunc(value as SharpObject) };

				if (value is SharpObjectList)
				{
					var valueList = value as SharpObjectList;
					return valueList.Select(initFunc);
				}

				return new List<T>();
			}

			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.GetMemberObjectEnumerable(nextPath, initFunc);

			return new List<T>();
		}

		public IList<T> GetMemberObjectList<T>(string path, Func<SharpObject, T> initFunc)
		{
			CheckExpand();
			var name = GetMemberName(path);

			if (name != null)
			{
				var value = ExpandMemberValue(name);
				if (value is SharpObject)
				{
					return new List<T> { initFunc(value as SharpObject) };
				}
				
				if (value is SharpObjectList)
				{
					var valueList = value as SharpObjectList;
					return valueList.Select(initFunc).ToList();
				}

				return new List<T>();
			}


			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.GetMemberObjectList(nextPath, initFunc);

			return new List<T>();
		}

		public T GetMemberObject<T>(string path, Func<SharpObject, T> initFunc)
		{
			CheckExpand();
			var name = GetMemberName(path);
			if (name != null)
			{
				var value = ExpandMemberValue(name);
				if (value is SharpObject)
				{
					return initFunc(value as SharpObject);
				}
				else if (value is SharpObjectList)
				{
					var valueList = value as SharpObjectList;
					return valueList.Select(initFunc).FirstOrDefault();
				}

				return default(T);
			}

			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
			{
				if (nextPath == null)
					return initFunc(obj);

				return obj.GetMemberObject<T>(nextPath, initFunc);
			}

			return default(T);
		}

		public bool AnyMemberContains(string path, string field, object value)
		{
			if (!_members.ContainsKey(path)) return false;

			var obj = _members[path];

			if (obj is SharpObject)
			{
				var cv = ((SharpObject) obj)[field];
				if (cv.Equals(value))
					return true;
			}

			if (obj is SharpObjectList)
			{
				if (((SharpObjectList)obj).Any(child => child[field].Equals(value)))
					return true;
			}

			return false;
		}
		public IEnumerable<string> OrderedKeys(List<string> required = null)
		{
			var names = new HashSet<string>();

			// Start with the required fields in required order
			if (required != null)
			{
				foreach (var field in required)
				{
					if (_members.ContainsKey(field))
						names.Add(field);

					yield return field;
				}
			}
			
			if (names.Count == _members.Count)
				yield break;

			// Then return single value fields in sorted order
			foreach (var item in _members.Where(x => !(x.Value is SharpObject || x.Value is SharpObjectList))
				.OrderBy(x => x.Key).Where(item => !names.Contains(item.Key)))
			{
				names.Add(item.Key);
				yield return item.Key;
			}

			// Then return arrays of simple values in sorted order
			foreach (var item in _members.Where(x => x.Value is SharpObjectList && ((SharpObjectList) x.Value).HasOnlyValue)
				.OrderBy(x => x.Key).Where(item => !names.Contains(item.Key)))
			{
				names.Add(item.Key);
				yield return item.Key;
			}

			// Then return lists of objects
			foreach (var item in _members.Where(x => x.Value is SharpObjectList)
				.OrderBy(x => x.Key).Where(item => !names.Contains(item.Key)))
			{
				names.Add(item.Key);
				yield return item.Key;
			}

			// Then return everything else
			foreach (var item in _members.Where(x => !names.Contains(x.Key) && x.Value is SharpObjectList).OrderBy(x => x.Key))
			{
				yield return item.Key;
			}
		}

		public SharpObject  FormatMemberValues(ISharpObjectFormat formatter, bool recursive = false)
		{
			CheckExpand();
			foreach (var key in _members.Keys.ToList())
			{
				var val = ExpandMemberValue(key);
				if (val is string)
				{
					if (IsFormatRequired((string) val))
						_members[key] = formatter.Format((string) val);
				}

				if (val is SharpObject)
				{
					var obj = (SharpObject) val;
					if (obj.HasOnlyValue)
					{
						var objval = obj.GetValue();
						if (objval is string)
						{
							if (IsFormatRequired((string)objval))
								_members[key] = formatter.Format((string)objval);
						}
					}
					else if (recursive)
					{
						obj.FormatMemberValues(formatter, true);
					}
				}

				if (val is SharpObjectList && recursive)
				{
					foreach (var obj in (SharpObjectList) val)
					{
						obj.FormatMemberValues(formatter, true);
					}
				}
			}

			return this;
		}

		public IEnumerable<KeyValuePair<string, object>> EnumerateMemberValue(bool collapseList=false)
		{
			CheckExpand();
			foreach (var key in _members.Keys.ToList())
			{
				object val = ExpandMemberValue(key);
				if (!(val is SharpObject) && !(val is SharpObjectList))
					yield return new KeyValuePair<string, object>(key, val);

				if (val is SharpObject)
				{
					var obj = (SharpObject) val;
					if (obj.HasOnlyValue)
						yield return new KeyValuePair<string, object>(key, obj.GetValue());
				}

				if (val is SharpObjectList)
				{
					var ls = (SharpObjectList) val;
					if (ls.HasOnlyValue && collapseList)
					{
						yield return new KeyValuePair<string, object>(key, ls.GetValueList());
					}
				}
			}
		}

		public bool TryParseEnum<T>(string path, out T result, string defaultValue = null) where T : struct
		{
			
			if (!ContainsKey(path)) 
			{
				if (String.IsNullOrEmpty(defaultValue))
				{
					result = default(T);
					return false;
				}

				return Enum.TryParse(defaultValue, true, out result);
			}

			var value = GetString(path);
			return Enum.TryParse(value, true, out result);
		}

		public IEnumerable<KeyValuePair<string, SharpObject>> EnumerateMemberObject(bool expandList=false)
		{
			CheckExpand();
			foreach (var key in _members.Keys.ToList())
			{
				object val = ExpandMemberValue(key);
				if (val is SharpObject)
				{
					var obj = (SharpObject) val;
					if (!obj.HasOnlyValue)
						yield return new KeyValuePair<string, SharpObject>(key, (SharpObject) val);
				}
				else if (val is SharpObjectList && expandList)
				{
					var ls = (SharpObjectList)val;
					if (!ls.HasOnlyValue)
					{
						foreach (var innerObj in (SharpObjectList) val)
						{
							yield return new KeyValuePair<string, SharpObject>(key, innerObj);
						}
					}
				}
			}
		}

		public IEnumerable<KeyValuePair<string, SharpObjectList>> EnumerateMemberObjectList(bool collapseList=false)
		{
			CheckExpand();
			foreach (var key in _members.Keys.ToList())
			{
				object val = ExpandMemberValue(key);
				if (val is SharpObjectList)
				{
					var ls = (SharpObjectList) val;
					if (!collapseList || !ls.HasOnlyValue)
					yield return new KeyValuePair<string, SharpObjectList>(key, ls);
				}
			}
		}

		public void IterateEach(Action<string, object> handleFunc, List<string> required = null)
		{
			CheckExpand();
			foreach (var field in OrderedKeys(required))
			{
				object val = ExpandMemberValue(field);
				handleFunc(field, val);
			}
		}

		public void IterateExact(Action<string, object> handleFunc, IEnumerable<string> fields = null)
		{
			CheckExpand();
			foreach (var field in fields)
			{
				object val = ExpandMemberValue(field);
				handleFunc(field, val);
			}
		}

		#endregion

		#region Properties

		public int ColumnId
		{
			get { return _columnId; }
			set { _columnId = value; }
		}

		internal SharpNodeRow GetRow()
		{
			return _source;
		}

		#endregion
		
		#region Object Value Interface
		
		public bool HasOnlyValue
		{
			get
			{
				lock (_sync)
				{
					if (!_isExpanded)
						ExpandObject();
				}
				return _members.Count == 1 && _members.ContainsKey("#");
			}
		}

		public bool HasValue
		{
			get
			{
				lock (_sync)
				{
					if (!_isExpanded)
						ExpandObject();
				}

				return _members.ContainsKey("#");
			}
		}

		public object GetValue()
		{
			lock (_sync)
			{
				if (!_isExpanded)
					ExpandObject();
			}

			if (_members.ContainsKey("#"))
				return ToValue(_types["#"], _members["#"]);
			
			return null;
		}
		#endregion

		#region Object Conversion Interface

		//public string GetObjectHash(params string[] excludeFields)
		//{
		//	var obj = new SharpObject(this);
		//	foreach(var field in excludeFields)
		//		obj.Remove(field);

		//	using (var file = new SharpFile())
		//	{
		//		file.InsertOnSubmit("File", obj);
		//		file.SubmitChanges();
		//		var content = file.SaveConfigStream();
		//		return EncodedString.GetMD5Hash(content).Replace("-", "");
		//	}
		//}

		public void CopyTo(object item, ISharpObjectFormat source, params string[] fields)
		{
			var include = fields.Where(x => x.FirstOrDefault() != '!').ToList();
			var exclude = fields.Where(x => x.FirstOrDefault() == '!').Select(x => x.Substring(1)).ToList();
			CopyTo(item, source, include.Any() ? include : null, exclude.Any() ? exclude : null);
		}

		public void CopyTo(object item, params string[] fields)
		{
			var include = fields.Where(x => x.FirstOrDefault() != '!').ToList();
			var exclude = fields.Where(x => x.FirstOrDefault() == '!').Select(x => x.Substring(1)).ToList();
			CopyTo(item, null, include.Any() ? include : null, exclude.Any() ? exclude : null);
		}

		#region Convert String to Value Types
		
		protected object ConvertStringToValue(string type, string value)
		{
			if (type == "String") 
				return value;
			
			if (type == "FileInfo") 
				return new FileInfo(value);
			
			if (type == "DirectoryInfo") 
				return new DirectoryInfo(value);

			if (type == "DateTime")
			{
				DateTime dt;
				if (DateTime.TryParse(value, out dt)) return dt;
			}

			if (type == "TimeSpan")
			{
				TimeSpan ts;
				if (TimeSpan.TryParse(value, out ts)) return ts;
			}

			if (type == "Int16")
			{
				short d;
				if (short.TryParse(value, out d)) return d;
			}
			
			if (type == "Int32")
			{
				int d;
				if (int.TryParse(value, out d)) return d;
			}

			if (type == "Int64")
			{
				long d;
				if (long.TryParse(value, out d)) return d;
			}

			if (type == "Decimal")
			{
				decimal d;
				if (decimal.TryParse(value, out d)) return d;
			}

			if (type == "Float")
			{
				float d;
				if (float.TryParse(value, out d)) return d;
			}

			if (type == "Double")
			{
				double d;
				if (double.TryParse(value, out d)) return d;
			}

			if (type == "Boolean")
			{
				bool d;
				if (bool.TryParse(value, out d)) return d;
			}

			return value;
		}

		#endregion

		public void CopyTo(object item, ISharpObjectFormat source, IEnumerable<string> includeProperties = null,
			IEnumerable<string> excludeProperties = null)
		{
			var values = ToDictionary();
			if (source != null)
			{
				foreach (var prop in values.Where(x=>x.Value is string).ToList())
				{
					values[prop.Key] = SharpObject.Format(source, (string)prop.Value);
				}
			}
			var properties = values.Keys.ToList();
			if (includeProperties != null)
				properties = properties.Where(includeProperties.Contains).ToList();

			if (excludeProperties != null)
				properties = properties.Where(x => !excludeProperties.Contains(x)).ToList();

			
			if (item is SharpObject)
			{
				SharpObject itemObj = ((SharpObject) item);

				foreach (var field in properties)
				{
					itemObj[field] = values[field];
				}
			}
			else
			{
				var itemType = item.GetType();
					
				foreach (var property in itemType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
					.Where(x => properties.Contains(x.Name)))
				{
					var targetType = property.PropertyType;
					var val = values[property.Name];
					if (val is string)
						val = ConvertStringToValue(targetType.Name, (string) val);

					property.SetValue(item, val, null);
				}

				foreach (var field in itemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
							.Where(x => properties.Contains(x.Name)))
				{
					var targetType = field.FieldType;
					var val = values[field.Name];
					if (val is string)
						val = ConvertStringToValue(targetType.Name, (string)val);

					field.SetValue(item, val);
				}


				foreach (var property in itemType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
					.Where(x => properties.Contains(x.Name + "Enum")))
				{
					var targetType = property.PropertyType;
					if (!targetType.IsEnum) continue;

					var enumName = values[property.Name + "Enum"].ToString();
					var enumValue = Enum.Parse(targetType, enumName, true);
					property.SetValue(item, enumValue, null);
					
				}

				foreach (var field in itemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
							.Where(x => properties.Contains(x.Name + "Enum")))
				{
					var targetType = field.FieldType;
					if (!targetType.IsEnum) continue;

					var enumName = values[field.Name + "Enum"].ToString();
					var enumValue = Enum.Parse(targetType, enumName, true);
					field.SetValue(item, enumValue);
				}
			}
		}

		public static IEnumerable<SharpObject> CopyAll(IEnumerable<object> items, List<string> excludeFields = null )
		{
			return items.Select(x => Copy(x, excludeFields));
		}

		public static SharpObject CopyType(object source, string contentType)
		{
			return Copy(source).SetFieldValue("ContentType", contentType);
		}

		public static SharpObject GetChanges(SharpObject source, SharpObject update)
		{
			var result = new SharpObject();

			#region Get Value Type Field Changes
			
			foreach (var field in update.EnumerateMemberValue())
			{
				if (!source.ContainsKey(field.Key))
				{
					result[field.Key] = field.Value;
					continue;
				}

				var rv = EncodedString.Serialize(source[field.Key]);
				var uv = EncodedString.Serialize(field.Value);

				if (rv != uv)
					result[field.Key] = field.Value;
			}

			#endregion

			#region Get Changes From Single Object Fields
			
			foreach (var field in update.EnumerateMemberObject())
			{
				if (!source.ContainsKey(field.Key))
				{
					result[field.Key] = field.Value;
					continue;
				}

				// Source field may be a list so make sure the new item is unique to
				// all of them

				var rvls = new HashSet<string>();
				foreach (var rv in source.GetMemberObjectList(field.Key, o => o))
				{
					rvls.Add(EncodedString.Serialize(rv.ToDictionary()));
				}

				var uv = EncodedString.Serialize(field.Value.ToDictionary());

				if (!rvls.Contains(uv))
					result[field.Key] = field.Value;
			}

			#endregion

			#region Get Changes from List Object Fields
			
			foreach (var field in update.EnumerateMemberObjectList())
			{
				if (!source.ContainsKey(field.Key))
				{
					result[field.Key] = field.Value;
					continue;
				}

				// Source field may be a list so make sure the new fiels are
				// unique to all of the source fields

				var rvls = new HashSet<string>();
				foreach (var rv in source.GetMemberObjectList(field.Key, o => o))
				{
					rvls.Add(EncodedString.Serialize(rv.ToDictionary()));
				}

				foreach (var value in field.Value)
				{
					var uv = EncodedString.Serialize(value.ToDictionary());
					if (!rvls.Contains(uv))
						result.Add(field.Key, value);
				}
			}

			#endregion

			return result;
		}
		
		public static SharpObject Copy(FileInfo ex)
		{
			return Copy(new
			{
				ex.Name,
				ex.DirectoryName,
				ex.Length,
				ex.Exists,
				ex.IsReadOnly,
				Attributes = ex.Attributes.ToString(),
				ex.CreationTime,
				ex.LastAccessTime,
				ex.LastWriteTime
			});
		}

		public static SharpObject Copy(Exception ex)
		{
			var obj = Copy(new
			{
				ex.Message,
				ExceptionType = ex.GetType().ToString()
			});
			if (!String.IsNullOrEmpty(ex.Source))
				obj["Source"] = ex.Source;

			if (!String.IsNullOrEmpty(ex.StackTrace))
				obj["StackTrace"] = ex.StackTrace;

			if (ex.TargetSite != null)
				obj["TargetSite"] = ex.TargetSite.ToString();

			var aggEx = ex as AggregateException;

			if (aggEx != null)
			{
				obj["InnerException"] = aggEx.InnerExceptions.Select(Copy).ToList();
			}
			else if (ex.InnerException != null)
			{
				obj["InnerException"] = Copy(ex.InnerException);
			}

			var d = new SharpObject();
			foreach (object key in ex.Data.Keys)
				d[key.ToString()] = ex.Data[key];
			if (d.Count > 0)
				obj["Data"] = d;

			return obj;
		}

		public static SharpObject Copy(object item, params string[] fields)
		{
			var exclude = fields.Where(x => x.StartsWith("!")).Select(x => x.Substring(1)).ToList();
			var props = fields.Where(x => x.StartsWith("~")).Select(x => x.Substring(1)).ToList();
			var include = fields.Where(x => !x.StartsWith("!") && !x.StartsWith("~")).ToList();

			return Copy(item, exclude, include, props);
		}

		public static SharpObject Copy(object item, List<string> excludeFields = null, List<string> includeFields = null, List<string> enumerateProperties = null)
		{
			SharpObject result = null;
		

			if (item is ISharpObject)
				result = new SharpObject( ((ISharpObject) item).GetObject());

			else if (item is Exception)
				result = Copy((Exception) item);
			
			else if (item is FileInfo)
				result = Copy((FileInfo) item);

			else if (item is IDictionary<string, object>)
				result = new SharpObject((IDictionary<string, object>) item);
			
			else if (item is IDictionary)
			{
				var d = (IDictionary) item;
				var dd = new Dictionary<string, object>();
				foreach (var key in d)
					dd[key.ToString()] = d[key];

				result = new SharpObject(dd);
			}
			else if (item is DataSet)
				result = new SharpObject((DataSet) item);

			else if (item is DataRow)
				result = new SharpObject((DataRow) item);

			if (result != null)
			{
				if (includeFields != null && includeFields.Count > 0)
				{
					var included = new HashSet<string>(includeFields, StringComparer.OrdinalIgnoreCase);
					foreach (var inner in result.ToList())
					{
						if (!included.Contains(inner.Key))
							result.Remove(inner.Key);
					}
				}
				else if (excludeFields != null)
				{
					foreach (var field in excludeFields)
						result.Remove(field);
				}

				return result;
			}

			var excludeHash = new HashSet<string>(excludeFields ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			var includeHash = new HashSet<string>(includeFields ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			var propertyHash = new HashSet<string>(enumerateProperties ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
		
			var itemType = item.GetType();
			var obj = new SharpObject();

			var itemFields = includeHash.Count > 0 
				? itemType.GetFields().Where(x => includeHash.Contains(x.Name)).ToList()
				: itemType.GetFields().Where(x => !excludeHash.Contains(x.Name)).ToList();

			foreach (var field in itemFields)
			{
				object val = field.GetValue(item);
				
				if (IsValueType(val))
				{
					obj[field.Name] = val;
				}
				else if (IsSerializeStringType(val))
				{
					obj[field.Name] = val.ToString();
				}
				else
				{
					var prefix = field.Name + ".";
					var exls = excludeHash.Where(x => x.StartsWith(prefix)).Select(x => x.Substring(prefix.Length)).ToList();
					var inls = includeHash.Where(x => x.StartsWith(prefix)).Select(x => x.Substring(prefix.Length)).ToList();
					obj[field.Name] = Copy(val, exls, inls);
				}
			}

			var itemProperties =  includeHash.Count > 0
			 ? itemType.GetProperties().Where(x => includeHash.Contains(x.Name)).ToList()
			 : itemType.GetProperties().Where(x => !excludeHash.Contains(x.Name)).ToList();
			
			foreach(var property in itemProperties)
			{
				var indexParams = property.GetIndexParameters();
				if (indexParams.Length != 0) continue;
				
				object val = property.GetValue(item, null);
			
				if (IsValueType(val))
				{
					obj[property.Name] = val;
				}
				else if (IsSerializeStringType(val))
				{
					obj[property.Name] = val.ToString();
				}
				else
				{
					var prefix = property.Name + ".";
					var exls = excludeHash.Where(x => x.StartsWith(prefix)).Select(x => x.Substring(prefix.Length)).ToList();
					var inls = includeHash.Where(x => x.StartsWith(prefix)).Select(x => x.Substring(prefix.Length)).ToList();
					var propls = propertyHash.Where(x => x.StartsWith(prefix)).Select(x => x.Substring(prefix.Length)).ToList();
					if (val is IEnumerable<object>)
					{
						if (propertyHash.Contains(property.Name))
						{

							obj[property.Name] = ((IEnumerable<object>)val).Select(x => Copy(x, exls, inls, propls)).ToList();
						}
					}
					else
					{
						obj[property.Name] = Copy(val, exls, inls, propls);
					}

				}
			}

			return obj;
		}

		public static bool IsSerializeStringType(object value)
		{
			if (value is Guid) return true;
			if (value is Enum) return true;
			return false;
		}
		public static bool IsValueType (object value)
		{
			if (value == null) return true;

			if (value is char) return true;
			if (value is string) return true;
			if (value is int) return true;
			if (value is long) return true;
			if (value is decimal) return true;
			if (value is float) return true;
			if (value is double) return true;
			if (value is TimeSpan) return true;
			if (value is DateTime) return true;
			if (value is bool) return true;

			if (value is IEnumerable<char>) return true;
			if (value is IEnumerable<string>) return true;
			if (value is IEnumerable<int>) return true;
			if (value is IEnumerable<long>) return true;
			if (value is IEnumerable<decimal>) return true;
			if (value is IEnumerable<float>) return true;
			if (value is IEnumerable<double>) return true;
			if (value is IEnumerable<TimeSpan>) return true;
			if (value is IEnumerable<DateTime>) return true;
			if (value is IEnumerable<bool>) return true;
			
			return false;
		}

		public SharpObject GetObject()
		{
			return this;
		}
		
		public dynamic AsDynamic()
		{
			if (_wrapper == null)
				_wrapper = new SharpObjectDynamic(this);

			return _wrapper;
		}

		public string Serialize(bool indent=false)
		{
			return EncodedString.Serialize(ToDictionary(), indent ? 0 : -1);
		}

		public string SerializeFields(Dictionary<string, string> fields, bool indent = false)
		{
			var obj = new SharpObject();
			foreach (var item in fields)
			{
				var value = this[item.Value];
				if (value != null)
					obj[item.Key] = value;
			}
			return obj.Serialize(indent);
		}

		public static SharpObject ParseObject(string source)
		{
			var d = EncodedString.ParseObject(source);
			return new SharpObject(d);
		}

		public Dictionary<string, object> ToDictionary(params string[] excludeFields)
		{
			var result = new Dictionary<string, object>();

			lock (_sync)
			{
				if (!_isExpanded)
					ExpandObject();
			}

			foreach (var name in _members.Keys.ToList())
			{
				if (excludeFields.Length > 0 && excludeFields.Contains(name)) continue;

				var value = ExpandMemberValue(name);

				var itemObjectList = value as SharpObjectList;
				if (itemObjectList != null)
				{
					if (itemObjectList.HasOnlyValue)
						result[name] = itemObjectList.Select(x => x.GetValue()).ToList();
					else
						result[name] = itemObjectList.Select(x => x.ToDictionary(excludeFields)).ToList();
					
					continue;
				}

				var itemObject = value as SharpObject;
				if (itemObject != null)
				{
					result[name] = itemObject.ToDictionary(excludeFields);
					continue;
				}

				result[name] = ToValue(_types[name], value);
			}

			return result;
		}

		#endregion
		
		#region Value Type Conversion

		private object ToValue(SharpValueType type, object value)
		{
			if (value is string && type != SharpValueType.String)
				return SharpValue.ToValue((string)value, type, _autoType);
			
			return value;
		}

		internal SharpValueType GetType(string name)
		{
			CheckExpand();
			name = GetMemberName(name);
			if (name == null || !_types.ContainsKey(name))
				return SharpValueType.None;

			return _types[name];
		}

		#endregion

		#region Lazy Loading Objects and Columns

		internal bool IsExpanded
		{
			get
			{
				return _isExpanded;
			}
		}

		private void CheckExpand()
		{
			lock (_sync)
			{
				if (!_isExpanded)
					ExpandObject();
			}
		}

		private void ExpandObject()
		{
			lock (_sync)
			{
				if (_isExpanded)
					Clear();
				
				var cursor = new SharpRowCursor(_source);
				while (cursor.MoveNext())
				{
					if (cursor.Current.Node.IsValueNode)
					{
						AddValueMember(cursor.Current);
					}
					else
					{
						AddObjectMember(cursor.Current);
					}
				}

				_isExpanded = true;
			}
		}

		private object ExpandMemberValue(string name)
		{
			if (_collapsed.Contains(name))
			{
				var valueRow = _rows[name] as SharpValueRow;
				int column = _columns[name];

				if (valueRow != null)
				{
					object value = valueRow.GetColumnValue(column);
					if (_types.ContainsKey(name))
					{
						var t = _types[name];
						if (value is string)
							value = SharpValue.ToValue(value as string, t, false);
					}
					_members[name] = value;
					_collapsed.Remove(name);
				}
			}

			return _members.ContainsKey(name) ? _members[name] : null;
		}
		
		#endregion

		#region Member Name Aliases
		private void AddAliases(string name, string alias = null)
		{
			if (alias == null)
				alias = name;

			if (!_members.ContainsKey(alias) && !_names.ContainsKey(alias))
				_names[alias] = name;

			if (alias.Length > 0 && alias[0] == '@')
				AddAliases(name, alias.Substring(1));

			if (alias.Contains("_"))
				AddAliases(name, alias.Replace("_", ""));

		}

		private string GetMemberName(string name)
		{
			if (_members.ContainsKey(name))
				return name;

			if (_names.ContainsKey(name) && _members.ContainsKey(_names[name]))
				return _names[name];

			return null;
		}

		#endregion

		#region Private Add Member Methods
		
		private void AddValueMember(SharpNodeRow row, string name, SharpValueType type, object value, int columnId=-1, bool collapsed=false)
		{
			if (_members.ContainsKey(name))
			{
				// multiple value nodes with the same name will always be wrapped in a SharpObject
				// and put into SharpObjectList ... this is needed to write-back changes to the 
				// correct rows.

				var current = ExpandMemberValue(name);

				var currentObjectList = current as SharpObjectList;
				if (currentObjectList == null)
				{
					currentObjectList = new SharpObjectList(this, name);

					var currentObject = current as SharpObject;
					if (currentObject != null)
					{
						currentObjectList.Add(currentObject);
					}
					else
					{
						var prevObj = new SharpObject(_rows[name], current, _types[name]);
						prevObj.ColumnId = _columns[name];

						currentObjectList.Add(prevObj);
					}
				}

				if (columnId > -1)
				{
					var valueRow = row as SharpValueRow;
					if (valueRow != null)
						value = valueRow.GetColumnValue(columnId);
				}

				var nextObj = new SharpObject(row, value, type);
				nextObj.ColumnId = columnId;

				currentObjectList.Add(nextObj);
		
				_members[name] = currentObjectList;
				_types.Remove(name);
				_rows.Remove(name);
				_collapsed.Remove(name);
			}
			else
			{
				if (!collapsed)
				{
					switch (type)
					{
						case SharpValueType.Auto:
						case SharpValueType.None:
						case SharpValueType.String:
							_members[name] = value;
							break;
						default:
							_members[name] = SharpValue.ToValue(value.ToString(), type);
							break;
					}
				}
				else
				{
					_members[name] = value;
				}

				_types[name] = type;
				_rows[name] = row;
				_columns[name] = columnId;

				if (collapsed)
					_collapsed.Add(name);
				
				AddAliases(name);
			}
		}

		private void AddValueMember(SharpNodeRow row)
		{
			var node = row.Node;
			var valueRow = row as SharpValueRow;
			if (valueRow == null)
				return;
			
			if (node.IsSingleValueNode)
			{
				if (node.IsExpandedValueNode)
					AddValueMember(row, "#", node.ValueType, valueRow.Values[0]);
				else
					AddValueMember(row, node.Name, node.ValueType, valueRow.Values[0]);
			}
			else
			{
				var mapNode = node as SharpNodeMap;
				if (mapNode == null)
					return;

				int index = 0;
				foreach (var innerNode in mapNode.GetNodes())
				{
					var name = innerNode.IsExpandedValueNode ? "#" : innerNode.Name;
					AddValueMember(row, name, innerNode.ValueType, null, index, true);
					index++;
				}
			}
		}

		private void AddObjectMember(SharpNodeRow row)
		{
			var node = row.Node;
			var obj = new SharpObject(row);

			if (_members.ContainsKey(node.Name))
			{
				var current = _members[node.Name];
				var currentObjectList = current as SharpObjectList;
				if (currentObjectList == null)
				{
					currentObjectList = new SharpObjectList(this, node.Name);

					var currentObject = current as SharpObject;
					if (currentObject != null)
					{
						currentObjectList.Add(currentObject);
					}
					else
					{
						currentObject = new SharpObject(_rows[node.Name],
							_members[node.Name],
							_types.ContainsKey(node.Name) ? _types[node.Name] : SharpValueType.None);

						currentObjectList.Add(currentObject);
					}
				}

				currentObjectList.Add(obj);
				_members[node.Name] = currentObjectList;
			}
			else
			{
				if (node.IsListNodeType())
				{
					_members[node.Name] = new SharpObjectList(this, node.Name, new List<SharpObject> {obj});
				}
				else
				{
					_members[node.Name] = obj;	
				}
				
				AddAliases(node.Name);
			}
		}

		#endregion 

		#region Query Path
		
		private SharpObject ResolveQueryPath(string queryPath, out string nextPath)
		{

			var parts = queryPath.Split(_pathSep, 2);
			nextPath = parts.Length == 1 ? null : parts[1];

			string sIndex = null;
			int index = -1;

			string name = GetMemberName(parts[0]);
			if (name == null)
			{
				if (parts[0].Contains("["))
				{
					var match = _arrayIndex.Match(parts[0]);
					if (match.Success)
					{
						name = match.Groups[1].Value;
						// TODO: handle full query path
						if (!Int32.TryParse(match.Groups[2].Value, out index))
							sIndex = match.Groups[2].Value;
					}
				}
			}

			if (name != null)
			{
				var obj = ExpandMemberValue(name);
				if (obj is SharpObject)
				{
					return ((SharpObject)obj);
				}
				else if (obj is SharpObjectList)
				{
					var objList = (SharpObjectList)obj;

					if (sIndex == null)
						return objList[index];
					else
						return objList[sIndex];
				}
			}

			return null;
		}
		
		#endregion

		#region Implement IDictionary<string, object>

		public void Clear()
		{
			lock (_sync)
			{
				_members.Clear();
				_names.Clear();
				_types.Clear();

				_isExpanded = false;
			}
		}

		public int Count { get { return _members.Count; }
			private set { }
		}

		public bool IsReadOnly { get; private set; }

		public void Add(KeyValuePair<string, object> item)
		{
			Add(item.Key, item.Value);
		}

		public bool Remove(KeyValuePair<string, object> item)
		{
			return Remove(item.Key);
		}

		public void Add(string key, object value)
		{
			CheckExpand();

			var obj = value as SharpObject;
			if (obj == null)
			{

				if (IsValueType(value))
				{
					obj = new SharpObject();
					obj["#"] = value; 
				}
				else if (IsSerializeStringType(value))
				{
					obj = new SharpObject();
					obj["#"] = value.ToString(); 
				}
				else
				{
					obj = Copy(value);
				}
			}

			if (_members.ContainsKey(key))
			{
				var current = _members[key];
				var currentObjectList = current as SharpObjectList;
				if (currentObjectList == null)
				{
					currentObjectList = new SharpObjectList(this, key);

					var currentObject = current as SharpObject;
					if (currentObject != null)
					{
						currentObjectList.Add(currentObject);
					}
					else if (_rows.ContainsKey(key))
					{
						currentObject = new SharpObject(_rows[key],
							_members[key],
							_types.ContainsKey(key) ? _types[key] : SharpValueType.None);

						currentObjectList.Add(currentObject);
					}
					else
					{
						currentObject = new SharpObject();
						currentObject["#"] = _members[key];
						currentObjectList.Add(currentObject);
					}
				}

				currentObjectList.Add(obj);
				_members[key] = currentObjectList;
			}
			else
			{
				_members[key] = obj;
				AddAliases(key);
			}
		}

		public bool Remove(string key)
		{
			var name = GetMemberName(key);
			if (name != null)
				return _members.Remove(name);
			
			string nextPath;
			var obj = ResolveQueryPath(key, out nextPath);
			if (obj != null)
				return obj.Remove(nextPath);

			return false;
		}

		public bool Equals(SharpObject obj, params string[] excludeFields)
		{
			CheckExpand();

			if (obj.Count != Count) return false;

			foreach (var item in _members)
			{
				if (excludeFields.Contains(item.Key)) continue;

				if (!obj.ContainsKey(item.Key)) return false;
				var other = obj[item.Key];
				if (item.Value is SharpObject)
				{
					if (!(other is SharpObject)) return false;
					if (!((SharpObject) item.Value).Equals(other as SharpObject, excludeFields)) return false;
				}
				else if (item.Value is SharpObjectList)
				{
					if (!(other is SharpObjectList)) return false;

					var ls = (SharpObjectList) item.Value;
					var other_ls = (SharpObjectList) other;
					if (ls.Count != other_ls.Count) return false;

					for (int i = 0; i < ls.Count; i++)
					{
						if (!ls[i].Equals(other_ls[i], excludeFields)) return false;
					}
				}
				else
				{
					if (EncodedString.Serialize(item.Value) != EncodedString.Serialize(other))
						return false;
				}
			}

			return true;
		}

		public bool Contains(KeyValuePair<string, object> item)
		{
			CheckExpand();
			var name = GetMemberName(item.Key);
			if (name == null)
				return false;

			object value = ExpandMemberValue(name);
			
			if (!(value is SharpObject || value is SharpObjectList))
				value = ToValue(_types[name], value);

			return value == item.Value;
		}

		public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
		{
			foreach (var item in this)
			{
				array[arrayIndex++] = item;
			}
		}

		public SharpObject Clone(params string[] excludeFields)
		{
			return Clone(false, false, excludeFields);
		}

		public SharpObject Clone(bool removeAttributes=false, bool autoCaseFields=false, params string[] excludeFields)
		{
			var result = new SharpObject(ToDictionary());
			if (excludeFields.Length > 0)
				result.Remove(true, excludeFields);

			if (autoCaseFields)
			{
				result.AutoCaseFields(true, removeAttributes);
			}
			else if (removeAttributes)
			{
				result.RemoveAttributes(true);
			}

			return result;
		}

		public void AutoCaseFields(bool recursive, bool removeAttributes=false)
		{
			foreach (var field in EnumerateMemberValue())
			{
				var fieldname = EncodedString.ConvertToCamelCase(field.Key);
				if (removeAttributes && fieldname[0] == '@')
					fieldname = fieldname.Substring(1);

				var value = field.Value;
				Remove(field.Key); 
				this[fieldname] = value;
			}

			if (recursive)
			{
				foreach (var child in EnumerateMemberObject(true))
					child.Value.AutoCaseFields(true, removeAttributes);
			}
		}

		public void RemoveAttributes(bool recursive)
		{
			foreach (var field in EnumerateMemberValue().Where(x => x.Key.StartsWith("@")).ToList())
			{
				var value = field.Value;
				Remove(field.Key);
				this[field.Key.Substring(1)] = value;
			}

			if (recursive)
			{
				foreach (var child in EnumerateMemberObject(true))
					child.Value.RemoveAttributes(true);
			}
		}

		public void Remove(bool recursive, params string[] fields)
		{
			foreach (var field in fields)
				Remove(field);

			if (recursive)
			{
				foreach (var child in EnumerateMemberObject(true))
					child.Value.Remove(true, fields);
			}
		}

		public SharpObject Clone(object merge)
		{
			var result = new SharpObject(this);
			var obj = merge as SharpObject;
			if (obj == null && merge is ISharpObject)
				obj = ((ISharpObject) merge).GetObject();

			if (obj == null)
				obj = Copy(merge);

			result.MergeWith(obj);
			return result;
		}
		
		public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
		{
			CheckExpand();
			foreach (var name in _members.Keys.ToList())
			{
				var value = ExpandMemberValue(name);

				if (value is SharpObject || value is SharpObjectList)
					yield return new KeyValuePair<string, object>(name, value);
				else
					yield return new KeyValuePair<string, object>(name, ToValue(_types[name], value));
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public bool ContainsAllKeys(params string[] paths)
		{
			return paths.All(ContainsKey);
		}

		public bool ContainsAnyKey(params string[] paths)
		{
			return paths.Any(ContainsKey);
		}
		
		public bool ContainsKey(string path)
		{
			CheckExpand();
			var name = GetMemberName(path);
			if (name != null) 
				return true;

			string nextPath;
			var obj = ResolveQueryPath(path, out nextPath);
			if (obj != null)
				return obj.ContainsKey(nextPath);
		
			return false;
		}
		
		public bool TryGetValue(string key, out object value)
		{
			CheckExpand(); 
			
			value = null;
			var name = GetMemberName(key);
			if (name != null)
			{

				value = ExpandMemberValue(name);
				if (value is SharpObject || value is SharpObjectList)
					return true;

				value = ToValue(_types[name], value);
				return true;
			}

			string nextPath;
			var obj = ResolveQueryPath(key, out nextPath);
			if (obj != null)
				return obj.TryGetValue(nextPath, out value);
		
			return false;
		}

	
		public object this[string key]
		{
			get
			{
				CheckExpand();
				var name = GetMemberName(key);
				if (name != null)
				{
					var value = ExpandMemberValue(name);
					if (value is SharpObject || value is SharpObjectList)
						return value;
					else
						return ToValue(_types[name], value);
				}
				else
				{
					string next;
					var obj = ResolveQueryPath(key, out next);
					if (obj != null)
						return obj[next];
				}
				return null;
			}

			set
			{
				CheckExpand();
				var name = GetMemberName(key);

				if (name != null)
				{
					SetMember(name, value);
				}
				else
				{
					string next;
					var obj = ResolveQueryPath(key, out next);
					if (obj != null)
						obj[next] = value;
					else
						SetMember(key, value);
				}
			}
		}

		public SharpObject SetFieldValue(string path, object value)
		{
			this[path] = value;
			return this;
		}

		public void SetMemberValue(string name, object value)
		{
			_members[name] = value;
			_types[name] = SharpValue.GetValueType(value);
		}

		public void SetMemberObject(string name, SharpObject value)
		{
			_members[name] = value;
		}

		private void SetMember(string name, object value)
		{
			if (value is ISharpObject)
			{
				_members[name] = ((ISharpObject) value).GetObject();
			}
			else if (value is IEnumerable<ISharpObject>)
			{
				_members[name] = new SharpObjectList(this, name, (IEnumerable<ISharpObject>) value);
			}
			else if (value is IDictionary<string, object>)
			{
				_members[name] = new SharpObject( (IDictionary<string, object>)value);
			}
			else if (value is IEnumerable<IDictionary<string, object>>)
			{
				var ls = (IEnumerable<IDictionary<string, object>>) value;
				_members[name] = new SharpObjectList(this, name, ls.Select(x=>new SharpObject(x)));
			}
			else if (value is IEnumerable<int>)
			{
				var ls = (IEnumerable<int>) value;
				_members[name] = new SharpObjectList(this, name,
					ls.Select(x => new SharpObject(
						new Dictionary<string, object> {{"#", x}})));
			}
			else if (value is IEnumerable<string>)
			{
				var ls = (IEnumerable<string>)value;
				_members[name] = new SharpObjectList(this, name,
					ls.Select(x => new SharpObject(
						new Dictionary<string, object> {{ "#", x }})));
			}
			else if (value is IEnumerable<object>)
			{
				var ls = (IEnumerable<object>) value;
				var items = new List<SharpObject>();
				foreach (var obj in ls)
				{
					if (IsValueType(obj))
					{
						var v = new SharpObject();
						v["#"] = obj;
						items.Add(v);
					}
					else if (IsSerializeStringType(obj))
					{
						var v = new SharpObject();
						v["#"] = obj.ToString();
						items.Add(v);
					}
					else
					{
						items.Add(Copy(obj));
					}
				}
				_members[name] = new SharpObjectList(this, name, items);
			}
			else
			{
				if (IsValueType(value))
				{
					_members[name] = value;
					_types[name] = new SharpValue(value).Type;
				}
				else if (IsSerializeStringType(value))
				{
					var s = value.ToString();
					_members[name] = s;
					_types[name] = new SharpValue(s).Type;
				}
				else
				{
					_members[name] = Copy(value);
				}
			}

			if (_collapsed.Contains(name))
				_collapsed.Remove(name);

			if (_rows.ContainsKey(name))
			{
				_rows[name].OnValueChanged(_columns[name], _members[name]);
			}
			else if (_source != null)
			{
				_source.OnMemberChanged(this, name, _members[name]);
			}
		}

		internal void OnMemberChanged(string name, object value)
		{
			if (_source != null)
			{
				_source.OnMemberChanged(this, name, value);
			}
		}

		public ICollection<string> Keys
		{
			get
			{
				CheckExpand();
				return _members.Keys.ToList();
			}
		}

		public ICollection<object> Values
		{
			get
			{
				CheckExpand();

				var result = new List<object>();
				foreach (var item in _members)
				{
					if (item.Value is SharpObject || item.Value is SharpObjectList)
						result.Add(item.Value);
					else
						result.Add(ToValue(_types[item.Key], item.Value));
				}
				return result;
			}
		}
		#endregion

		#region Format Parsing

		public static bool IsFormatRequired(string source)
		{
			if (String.IsNullOrWhiteSpace(source)) return false;

			return source.IndexOf("{", StringComparison.InvariantCulture) != -1;
		}

		public bool ContainsField(string key)
		{
			return ContainsKey(key);
		}

		public object GetFieldValue(string key)
		{
			return this[key];
		}

		#region SharpObjectFormatContext
		
		private class SharpObjectFormatContext : IEnumerator<ISharpObjectFormat>
		{
			public List<ISharpObjectFormat> Source = new List<ISharpObjectFormat>();
			
			public ISharpObjectFormat Current { get { return Source[Index]; } }
			public StringBuilder Result = new StringBuilder();
			public string Tag = "";
			public string Field = "";
			public int StartPosition = 0;
			public int Index = 0;
			public string Value = "";
			public bool HasValue = false;

			public bool AlwaysInclude = false;
			public bool IsOpen = true;

			public SharpObjectFormatContext()
			{
			}

			public SharpObjectFormatContext(ISharpObjectFormat source)
			{
				Source.Add(source);
				Tag = "with";
			}

			public SharpObjectFormatContext(IEnumerable<ISharpObjectFormat> sourceList)
			{
				Source.AddRange(sourceList);
				Tag = "each";
			}

			public bool IsValueValid
			{
				get
				{
					if (Current == null)
						return false;

					if (!HasValue) 
						return false;

					var invert = Field.StartsWith("!");
					var fieldName = invert ? Field.Substring(1) : Field;
					if (!Current.ContainsField(fieldName))
						return invert ^ false;

					var fieldMember = "#";

					if (fieldName.Contains("/"))
					{
						var fieldParts = fieldName.Split(new char[] {'/'}, 2).ToList();
						if (fieldParts.Count > 1)
						{
							fieldName = fieldParts[0].Trim();
							fieldMember = fieldParts[1].Trim();
						}
					}

					var checkValues = EncodedString.ParseList(Value);
			
					foreach (var value in checkValues)
					{
						bool result;
						var fieldValue = Current.GetFieldValue(fieldName);
						if (fieldValue is IEnumerable<SharpObject>)
						{
							var source = (IEnumerable<SharpObject>) fieldValue;
							foreach (var sourceItem in source)
							{
								if (sourceItem.ContainsKey(fieldMember))
								{
									result = invert ^ String.Equals(sourceItem[fieldMember].ToString(), value.ToString(), StringComparison.OrdinalIgnoreCase);
								}
								else
								{
									result = invert ^ false;
								}

								if (result) return true;
							}
						}
						else if (fieldValue is IEnumerable<object>)
						{
							result = invert ^
									 ((IEnumerable<object>) fieldValue).Any(
										 x => String.Equals(x.ToString(), value.ToString(), StringComparison.OrdinalIgnoreCase));
							if (result) return true;
						}
						else
						{
							result = invert ^ String.Equals(fieldValue.ToString(), value.ToString(), StringComparison.OrdinalIgnoreCase);
							if (result)
								return true;
						}
					}

					return false;
				}
			}

			public bool IsFieldValid
			{
				get
				{
					if (Current == null)
						return false;

					var invert = Field.StartsWith("!");
					var fieldName = invert ? Field.Substring(1) : Field;
					if (!Current.ContainsField(fieldName))
						return invert ^ false;

					var value = Current.GetFieldValue(fieldName);
					
					if (value is string)
					{
						return invert ^ (!String.IsNullOrEmpty((string)value));
					}

					if (value is bool)
					{
						return invert ^ ((bool) value);
					}
					
					if (value is IEnumerable<object>)
					{
						if (!((IEnumerable<object>) value).Any())
							return invert ^ false;
						return invert ^ true;
					}

					return invert ^ (value.ToString() != "0");
				}
			}

			#region IEnumerator
			
			public bool MoveNext()
			{
				if (Index >= Source.Count) return false;
				Index++;
				return Index < Source.Count;
			}

			public void Reset()
			{
				Index = 0;
			}

			object IEnumerator.Current
			{
				get { return Current; }
			}

			public void Dispose()
			{
			}
			#endregion
		}

		#endregion

		public static string FormatString(object source, string format, params object[] fields)
		{
			return Copy(source).Format(format, fields);
		}

		public static string Format(ISharpObjectFormat source, string format, params object[] fields)
		{
			Stack<SharpObjectFormatContext> context = new Stack<SharpObjectFormatContext>();
			
			context.Push(new SharpObjectFormatContext(source));
			
			StringBuilder tag = new StringBuilder();

			bool parsingTag = false;

			format = format ?? String.Empty;

			for (int i = 0; i < format.Length; i++)
			{
				var c = format[i];
				var nc = (i + 1 < format.Length) ? format[i + 1] : '\0';
				SharpObjectFormatContext top = context.Peek();

				if (parsingTag)
				{
					if (c == '}')
					{
						parsingTag = false;
						var tagName = tag.ToString().Trim();
						
						if (tagName.StartsWith("/"))
						{
							#region Handle Closing Tags

							tagName = tagName.ToLower().Substring(1);
							if (String.Compare(tagName, top.Tag, StringComparison.OrdinalIgnoreCase) != 0)
							{
								throw new FormatException(String.Format("Mismached closing tag: Found '{0}' expected '{1}' : {2}", tagName, top.Tag, format));
							}

							if (context.Count <= 1)
							{
								throw new FormatException(String.Format("Too many closing tags: {0}", format));
							}

							var last = context.Pop();
							top = context.Peek();

							switch (last.Tag)
							{
								case "even":
								case "odd":
								case "last":
								case "middle":
								case "first":
									if (last.AlwaysInclude)
										top.Result.Append(last.Result);
									break;
								
								case "with":
									top.Result.Append(last.Result);
									break;

								case "each":
									top.Result.Append(last.Result);
									if (last.MoveNext())
									{
										last.Result.Clear();
										i = last.StartPosition;
										context.Push(last);
									}
									break;
								case "if":
									if (last.IsOpen && (last.AlwaysInclude || last.IsFieldValid))
									{
										top.Result.Append(last.Result);
									}
									break;
								case "switch":
									if (last.IsOpen && (last.AlwaysInclude || last.IsValueValid))
									{
										top.Result.Append(last.Result);
									}
									
									break;
							}
#endregion
						}

						else if (tagName.StartsWith("#"))
						{
							#region Handle Opening Tags

							var tagParts = tagName.Split(new[] {' '}, 2);
							
							var nextContext = new SharpObjectFormatContext();
							nextContext.StartPosition = i;

							switch (tagParts[0].Substring(1).ToLower().Trim())
							{
								
								#region Value Tags
								case "index":
									top.Result.Append(top.Index + 1);
									break;

								case "count":
									if (tagParts.Length > 1)
									{
										var countParts = tagParts[1].Split(',').Select(x => x.Trim()).Where(x => !String.IsNullOrEmpty(x)).ToList();
										var countSource = countParts[0];
										object countSourceValue = null;
										int countSourceArg;
										if (Int32.TryParse(countSource, out countSourceArg))
										{
											if (countSourceArg < fields.Length)
												countSourceValue = fields[countSourceArg];
										}
										else if (top.Current.ContainsField(countSource))
										{
											countSourceValue = top.Current.GetFieldValue(countSource);
										}

										string countTotal;
										int count = 0;
										if (countSourceValue is int || countSourceValue is string)
										{
											countTotal = countSourceValue.ToString();
										}
										else if (countSourceValue is IEnumerable)
										{
											countTotal = ((IEnumerable) countSourceValue).Cast<object>().Count().ToString();
										}
										else
										{
											countTotal = (countSourceValue ?? "").ToString();
										}

										var counter = new StringBuilder();

										top.Result.Append(countTotal);

										if (countParts.Count > 1)
										{
											counter.Append(countParts[1]);
										}

										if (!Int32.TryParse(countTotal, out count))
											count = 0;

										if (countParts.Count > 2 && count != 1)
										{
											var s = countParts[2];
											if (s.StartsWith("~"))
											{
												counter.Clear();
												counter.Append(s.Substring(1));
											}
											else
											{
												counter.Append(s);
											}
										}

										if (counter.Length > 0)
											top.Result.Append(" ").Append(counter);
									}
									break;
								#endregion
								
								#region Position Tags

								case "!first":
									nextContext.Tag = "first";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = top.Index != 0;
									context.Push(nextContext);
									break;

								case "first":
									nextContext.Tag = "first";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = top.Index == 0;
									context.Push(nextContext);
									break;

								case "last":
									nextContext.Tag = "last";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = top.Index == top.Source.Count - 1;
									context.Push(nextContext);
									break;

								case "!last":
									nextContext.Tag = "last";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = top.Index != top.Source.Count - 1;
									context.Push(nextContext);
									break;

								case "middle":
									nextContext.Tag = "middle";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = top.Index > 0 && top.Index < top.Source.Count - 1;
									context.Push(nextContext);
									break;

								case "!middle":
									nextContext.Tag = "middle";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = !(top.Index > 0 && top.Index < top.Source.Count - 1);
									context.Push(nextContext);
									break;

								case "odd":
									nextContext.Tag = "odd";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = (top.Index + 1) % 2 == 1;
									context.Push(nextContext);
									break;

								case "even":
									nextContext.Tag = "even";
									nextContext.Source.Add(top.Current);
									nextContext.AlwaysInclude = (top.Index + 1) % 2 == 1;
									context.Push(nextContext);
									break;

								#endregion

								#region Conditional Tags

								#region Switch-Case-Default

								case "switch":
									nextContext.Tag = "switch";
									nextContext.Source.Add(top.Current);
									nextContext.Field = tagParts.Length > 1 ? tagParts[1].Trim() : "#";
									context.Push(nextContext);
									break;

								case "case":
									if (top.Tag != "switch")
										throw new FormatException(String.Format("'case' tag must be inside 'switch' block: {0}", format));
									if (tagParts.Length < 2)
										throw new FormatException(String.Format("'case' tag must have a value: {0}", format));

									if (top.IsOpen)
									{
										if (top.IsValueValid)
										{
											context.Pop();
											var last = context.Peek();
											last.Result.Append(top.Result);
											context.Push(top);
											top.IsOpen = false;
										}
										top.Value = tagParts[1].Trim();
										top.HasValue = true;
									}

									top.Result.Clear();
									break;

								case "default":
									if (top.Tag != "switch")
										throw new FormatException(String.Format("'default' tag must be inside 'switch' block: {0}", format));

									if (top.IsOpen)
									{
										if (top.IsValueValid)
										{
											context.Pop();
											var last = context.Peek();
											last.Result.Append(top.Result);
											context.Push(top);

											top.AlwaysInclude = false;
											top.IsOpen = false;
										}
										else
										{
											top.AlwaysInclude = true;
											top.IsOpen = true;
										}
									}
									top.Result.Clear();
									break;
								#endregion

								#region If-ElseIf-Else

								case "if":
									nextContext.Tag = "if";
									nextContext.Source.Add(top.Current);
									nextContext.Field = tagParts.Length > 1 ? tagParts[1].Trim() : "#";
									context.Push(nextContext);
									break;

								case "elseif":
									if (top.Tag != "if")
										throw new FormatException(String.Format("'elseif' tag must be inside 'if' block: {0}", format));

									if (top.IsOpen)
									{
										if (top.IsFieldValid)
										{
											context.Pop();
											var last = context.Peek();
											last.Result.Append(top.Result);
											context.Push(top);

											top.IsOpen = false;
										}

										top.Field = tagParts.Length > 1 ? tagParts[1].Trim() : "#";
									}

									top.Result.Clear();
									break;

								case "else":
									if (top.Tag != "if")
										throw new FormatException(String.Format("'else' tag must be inside 'if' block: {0}", format));
									if (top.IsOpen)
									{
										if (top.IsFieldValid)
										{
											context.Pop();
											var last = context.Peek();
											last.Result.Append(top.Result);
											context.Push(top);

											top.AlwaysInclude = false;
											top.IsOpen = false;
										}
										else
										{
											top.AlwaysInclude = true;
											top.IsOpen = true;
										}
									}
									top.Result.Clear();
									break;
								#endregion

								#endregion

								#region Looping Tags

								case "each":
									{
										var next = tagParts.Length > 1 ? top.Current.GetFieldValue(tagParts[1].Trim()) : null;

										if (next is ISharpObjectFormat)
										{
											nextContext.Source.Add((ISharpObjectFormat)next);
										}
										else if (next is SharpObjectList)
										{
											nextContext.Source.AddRange((SharpObjectList)next);
										}
										else if (next is IEnumerable<ISharpObjectFormat>)
										{
											nextContext.Source.AddRange((IEnumerable<ISharpObjectFormat>)next);
										}
										else if (next is IEnumerable<ISharpObject>)
										{
											nextContext.Source.AddRange(
												((IEnumerable<ISharpObject>)next).Select(x => x.GetObject()));
										}
										else
										{
											throw new DataException(String.Format("Expected '{0}' to be a collection of SharpObjects. Found '{1}'", tagParts[0],
												next == null ? "null" : next.GetType().ToString()));
										}

										nextContext.Tag = "each";
										context.Push(nextContext);
									}
									break;
								#endregion

								#region Scoping Tags
								case "with":
									{
										var next = tagParts.Length > 1 ? top.Current.GetFieldValue(tagParts[1].Trim()) : null;

										if (next is ISharpObjectFormat)
										{
											nextContext.Source.Add((ISharpObjectFormat)next);
										}
										else
										{
											throw new DataException(String.Format("Expected '{0}' to be a SharpObject. Found '{1}'", tagParts[0],
												next == null ? "null" : next.GetType().ToString()));
										}

										nextContext.Tag = "with";
										context.Push(nextContext);
									}
									break;
								#endregion
							}
							#endregion
						}
						else
						{
							#region Handle Simple Tags or indexed Field Values

							// TODO: Convert to regex
							int tagIndex = 0;
							var tagIndexParts = tagName.Split(new[] {':'}, 2);

							if (Int32.TryParse(tagIndexParts[0], out tagIndex))
							{
								if (tagIndex >= fields.Length)
									top.Result.Append(String.Format("{{{0}}}", tagName));
								else
								{
									var tagValue = fields[tagIndex];

									if (tagIndexParts.Length > 1)
									{
										var tagFormat = String.Format("{{0:{0}}}", tagIndexParts[1]);
										top.Result.Append(String.Format(tagFormat, tagValue));
									}
									else
									{
										//top.Result.Append(string.Format("{{{0}}}", tagName));
										top.Result.Append(tagValue);
									}
								}
							}
							else
							{
								var reStart = tagName.IndexOf('^');
								string reMatch = null;
								if (reStart >= 0)
								{
									reMatch = tagName.Substring(reStart+1);
									tagName = tagName.Substring(0, reStart);
								}
								
								if (top.Current.ContainsField(tagName))
								{
									var tagValue = top.Current.GetFieldValue(tagName);
									if (reStart >= 0)
									{
										var re = new Regex(reMatch, RegexOptions.IgnoreCase);
										var m = re.Match(tagValue.ToString());
										if (m.Success && m.Groups.Count > 0)
										{
											tagValue = m.Groups[1].Value;
										}
									}

									top.Result.Append(tagValue);
								}

								else if (tagIndexParts.Length > 1 && top.Current.ContainsField(tagIndexParts[0]))
								{
									var tagValue = top.Current.GetFieldValue(tagIndexParts[0]);
									var fieldFormat = String.Format("{{0:{0}}}", tagIndexParts[1]);
									try
									{
										tagValue = String.Format(fieldFormat, tagValue);
									}
									catch
									{
										tagValue = String.Format("{{{0}}}", tagName);
									}
									top.Result.Append(tagValue);
								}
								else
								{
									top.Result.Append(String.Format("{{{0}}}", tagName));
								}
							}

							#endregion
						}

						tag.Clear();
						continue;
					}
					
					tag.Append(c);
				}
				else
				{
					#region Handle Double Braces
					
					if (c == '{' && nc == '{')
					{
						top.Result.Append('{');
						i++;
						continue;
					}
					
					if (c == '}' && nc == '}')
					{
						top.Result.Append('}');
						i++;
						continue;
					}
					#endregion

					if (c == '{')
					{
						parsingTag = true;
						continue;
					}

					top.Result.Append(c);

				}
			}

			return context.Peek().Result.ToString();
		}

		#endregion

		//public static SharpObject LoadXml(string filename)
		//{
		//	if (String.IsNullOrEmpty(filename) || !File.Exists(filename))
		//		return new SharpObject();

		//	var file = new SharpFile();
		//	file.LoadXml(filename);
		//	return file.SelectRoot();
		//}

		//public static SharpObject LoadXml(string filename, Encoding encoding)
		//{
		//	if (String.IsNullOrEmpty(filename) || !File.Exists(filename))
		//		return new SharpObject();

		//	var file = new SharpFile();
		//	file.LoadXml(filename, encoding);
		//	return file.SelectRoot();
		//}

		//public static SharpObject LoadXmlString(string content)
		//{
		//	var file = new SharpFile();
		//	file.LoadXmlString(content);
		//	return file.SelectRoot();
		//}

		//public static SharpObject LoadConfig(string filename)
		//{
		//	if (String.IsNullOrEmpty(filename) || !File.Exists(filename))
		//		return new SharpObject();

		//	var file = new SharpFile();
		//	file.LoadConfig(filename);
		//	return file.SelectRoot();
		//}

		//public void SaveConfig(string filename, string rootNode)
		//{
		//	using (var file = new SharpFile())
		//	{
		//		file.InsertOnSubmit(rootNode, this);
		//		file.SubmitChanges();
		//		file.SaveConfig(filename);
		//	}
		//}

		//public void SaveXml(string filename, string rootNode)
		//{
		//	using (var file = new SharpFile())
		//	{
		//		file.InsertOnSubmit(rootNode, this);
		//		file.SubmitChanges();
		//		file.SaveXml(filename);
		//	}
		//}

		public string ToXml(string rootNode)
		{
			return ToXml(rootNode, 0);
		}

		public DataSet ToDataSet(string datasetName, params string[] fields)
		{
			DataSet result = new DataSet(datasetName);

			var include = fields.Where(x => x.FirstOrDefault() != '!').ToList();
			var exclude = fields.Where(x => x.FirstOrDefault() == '!').Select(x => x.Substring(1)).ToList();
			
			foreach (var field in EnumerateMemberObjectList())
			{
				if (include.Count > 0 && !include.Contains(field.Key)) continue;
				if (exclude.Count > 0 && exclude.Contains(field.Key)) continue;

				var childfields =
					fields.Where(x => x.StartsWith(field.Key + ".") || x.StartsWith("!" + field.Key + ".")).ToArray();

				var dt = ToDataTable(field.Key, field.Value, childfields);
				result.Tables.Add(dt);
			}

			foreach (var field in EnumerateMemberObject())
			{
				if (include.Count > 0 && !include.Contains(field.Key)) continue;
				if (exclude.Count > 0 && exclude.Contains(field.Key)) continue;

				var childfields =
					fields.Where(x => x.StartsWith(field.Key + ".") || x.StartsWith("!" + field.Key + ".")).ToList();

				childfields.AddRange(fields.Where(x => x.StartsWith("*.")).Select(x => x.Substring(2)));
				childfields.AddRange(fields.Where(x => x.StartsWith("!*.")).Select(x => "!"+ x.Substring(3)));

				var dt = ToDataTable(field.Key, new [] { field.Value}, childfields.ToArray());
				result.Tables.Add(dt);
			}

			return result;
		}

		public static DataTable ToDataTable(string tableName, IEnumerable<SharpObject> rows, params string[] fields)
		{
			DataTable table = new DataTable(tableName);
			
			var include = fields.Where(x => x.FirstOrDefault() != '!').ToList();
			var exclude = fields.Where(x => x.FirstOrDefault() == '!').Select(x => x.Substring(1)).ToList();

			foreach (var row in rows)
			{
				Dictionary<string, object> rowdata = new Dictionary<string, object>();
				foreach (var col in row.EnumerateMemberValue(true))
				{
					if (include.Count > 0 && !include.Contains(col.Key))continue;
					if (exclude.Count > 0 && exclude.Contains(col.Key)) continue;

					if (!table.Columns.Contains(col.Key))
					{
						var dc = new DataColumn(col.Key, col.Value.GetType());
						table.Columns.Add(dc);
					}

					rowdata[col.Key] = col.Value;
				}

				var values = new List<object>();

				foreach (DataColumn column in table.Columns)
				{
					if (rowdata.ContainsKey(column.ColumnName))
						values.Add(rowdata[column.ColumnName]);
					else
						values.Add(column.DefaultValue);
				}

				table.Rows.Add(values.ToArray());
			}

			return table;
		}

		protected string ToXml(string node, int level)
		{
			StringBuilder sb = new StringBuilder();

			CheckExpand();
			foreach (var item in _collapsed.ToList())
				ExpandMemberValue(item);

			var attributes = _members.Where(x => x.Key.StartsWith("@")).OrderBy(x => x.Key)
				.Select(x => String.Format("{0}={1}", x.Key.Substring(1), EncodedString.EncodeQuotedString(x.Value.ToString())))
				.ToList();

			if (attributes.Count > 0)
				sb.Append(' ', level*4).Append("<").Append(node).Append(" ").Append(String.Join(" ", attributes)).Append(">");
			else
				sb.Append(' ', level * 4).Append("<").Append(node).Append(">");
			
			if (HasValue)
			{
				sb.Append(_members["#"]);
			}
			else
			{
				sb.AppendLine();
				foreach( var child in _members.Where(x => x.Key != "#" && !x.Key.StartsWith("@") ))
				{
					if (child.Value is SharpObject)
					{
						sb.Append(((SharpObject) child.Value).ToXml(child.Key, level + 1));
					}
					else if (child.Value is SharpObjectList)
					{
						var ols = (SharpObjectList) child.Value;
						if (ols.HasOnlyValue)
						{
							foreach (var item in (IEnumerable) ols.GetValueList())
							{
								string value;
								if (item is string)
									value = EncodedString.XmlEncode((string) item);
								else
									value = (item != null ? item.ToString() : String.Empty);

								sb.Append(' ', (level + 1) * 4).Append("<").Append(child.Key).Append(">").Append(value).Append("</").Append(child.Key).AppendLine(">");
							}
						}
						else
						{
							foreach (var childnode in (SharpObjectList)child.Value)
							{
								sb.Append(childnode.ToXml(child.Key, level + 1));
							}	
						}
					}
					else
					{
						string value;
						if (child.Value is string)
							value = EncodedString.XmlEncode((string)child.Value);
						else
							value = (child.Value != null ? child.Value.ToString() : String.Empty);

						sb.Append(' ', (level + 1)*4).Append("<").Append(child.Key).Append(">").Append(value).Append("</").Append(child.Key).AppendLine(">");
					}
				}
			}

			sb.Append(' ', level * 4).Append("</").Append(node).AppendLine(">");
			return sb.ToString(); 
		}

		private void WriteConfigValueLine(StreamWriter writer, string path, string name, object value, SharpValueType type, HashSet<string> schema, bool repeated = false)
		{
			var valuePath = String.Format("{0}/{1}", path, name);

			var sb = new StringBuilder();
			sb.Append("  ./");
			sb.Append(name);
			if (!schema.Contains(valuePath)  && (repeated || type != SharpValueType.None))
			{
				sb.Append("~");
				if (repeated) sb.Append("Repeated ");
				if (type != SharpValueType.None)
					sb.Append(type);
				schema.Add(valuePath);
			}
			sb.Append(" = ");
			sb.Append(SharpValue.EncodeValue(value, type));
			writer.WriteLine(sb);
		}

		private void WriteFlatConfigValueLine(StreamWriter writer, Dictionary<string, object> fields,
			List<string> columns, ref int columnIndex, char delimiter = ',')
		{
			Dictionary<string, string> rowValues = new Dictionary<string, string>();
			foreach (var field in fields)
			{
				var vt = _types.ContainsKey(field.Key) ? _types[field.Key] : new SharpValue(field.Value).Type;
				rowValues[field.Key] = SharpValue.EncodeValue(field.Value, vt, delimiter:delimiter);
			}

			StringBuilder sb = new StringBuilder();
			int fieldCount = 0;
			foreach (var col in columns)
			{
				sb.Append(delimiter);
				if (rowValues.ContainsKey(col))
				{
					sb.Append(rowValues[col]);
					fieldCount ++;
				}
			}

			if (fieldCount != fields.Count)
			{
				var extraFields = fields.Keys.Where(x => !columns.Contains(x)).ToList();
				foreach (var col in extraFields)
				{
					columns.Add(col);
					sb.Append(delimiter);
					sb.Append(rowValues[col]);
				}

				columnIndex++;
				var schemaLine = new StringBuilder(String.Format("##{0}", columnIndex));
				foreach (var field in columns)
				{
					schemaLine.Append(delimiter);
					schemaLine.Append(field);
					var val = _members[field];
					var vt = _types.ContainsKey(field) ? _types[field] : new SharpValue(val).Type;
					if (vt != SharpValueType.None && vt != SharpValueType.String)
					{
						schemaLine.Append('~');
						schemaLine.Append(vt);
					}
				}
				
				writer.WriteLine(schemaLine);
			}
			writer.Write("#{0}", columnIndex);
			
			writer.WriteLine(sb);

		}

		//public IEnumerable<T> ImportMemberObjectList<T>(string path, string rootPath, Func<SharpObject, T> func, ISharpObjectFormat format = null)
		//{
		//	return GetMemberObjectList(path, o => ImportObject(o, rootPath, func, format));
		//}

		//public T ImportMemberObject<T>(string path, string rootPath, Func<SharpObject, T> func, ISharpObjectFormat format = null)
		//{
		//	return ImportMemberObjectList(path, rootPath, func, format).FirstOrDefault();
		//}

		//public IEnumerable<KeyValuePair<string, T>> ImportMemberObjects<T>(Func<string, SharpObject, bool> filter, string rootPath, Func<SharpObject, T> func, ISharpObjectFormat format = null)
		//{
		//	return EnumerateMemberObject(true)
		//				.Where(x => filter(x.Key, x.Value)).ToList()
		//				.Select(x => new KeyValuePair<string, T>(x.Key, ImportObject(x.Value, rootPath, func, format)));
		//}
		
		//public IEnumerable<KeyValuePair<string, T>> ImportMemberObjects<T>(Func<string, bool> filter, string rootPath, Func<SharpObject, T> func, ISharpObjectFormat format = null)
		//{
		//	return EnumerateMemberObject(true)
		//				.Where(x => filter(x.Key)).ToList()
		//				.Select(x => new KeyValuePair<string, T>(x.Key, ImportObject(x.Value, rootPath, func, format)));
		//}


		public IEnumerable<KeyValuePair<string, T>> SelectMemberObjects<T>(Func<string, bool> filter,
			Func<SharpObject, T> func)
		{
			return EnumerateMemberObject(true)
						.Where(x => filter(x.Key)).ToList()
						.Select(x => new KeyValuePair<string, T>(x.Key, func(x.Value)));
		}


		public IEnumerable<KeyValuePair<string, T>> SelectMemberObjects<T>(Func<string, SharpObject, bool> filter,
			Func<SharpObject, T> func)
		{
			return EnumerateMemberObject(true)
						.Where(x => filter(x.Key, x.Value)).ToList()
						.Select(x => new KeyValuePair<string, T>(x.Key, func(x.Value)));
		}

		public IEnumerable<KeyValuePair<string, T>> SelectMemberValues<T>(Func<string, object, bool> filter, Func<object, T> func)
		{
			return EnumerateMemberValue(true)
						.Where(x => filter(x.Key, x.Value))
						.Select(x => new KeyValuePair<string, T>(x.Key, func(x.Value)));
		}

		public IEnumerable<KeyValuePair<string, T>> SelectMemberValues<T>(Func<string, bool> filter, Func<object, T> func)
		{
			return EnumerateMemberValue(true)
						.Where(x => filter(x.Key))
						.Select(x => new KeyValuePair<string, T>(x.Key, func(x.Value)));
		}

		//public static T ImportObject<T>(SharpObject source, string rootPath, Func<SharpObject, T> func,
		//	ISharpObjectFormat format = null)
		//{
		//	var importPath = source.GetString("Import");
		//	if (importPath == null) return func(source);

		//	if ((importPath.StartsWith(@".\") || importPath.StartsWith(@"..\")) && rootPath != null)
		//	{
		//		importPath = Path.GetFullPath(Path.Combine(rootPath, importPath));
		//	}
		//	else if (format != null)
		//	{
		//		importPath = format.Format(importPath);
		//	}

		//	if (File.Exists(importPath))
		//	{
		//		var importObj = SharpObject.LoadConfig(importPath);
		//		source.CopyTo(importObj);
		//		return func(importObj);
		//	}

		//	return func(source);
		//}

		public bool AppendFlatConfig(StreamWriter writer, string path, Dictionary<string, List<string>> schema = null, 
				bool repeated = false, bool skipEmpty = false, bool skipNodePath = false,
				List<string> required = null )
		{
			bool allowSkipNextNode = true;
			HashSet<string> names = new HashSet<string>();
			if (schema == null) schema = new Dictionary<string, List<string>>();
			
			if (!schema.ContainsKey(path))
			{
				if (repeated)
					writer.WriteLine("{0}~Repeated", path);
				else
					writer.WriteLine(path);
				
				schema.Add(path, new List<string>());
			}
			else
			{
				if (!skipNodePath)
					writer.WriteLine(path);
			}

			var columns = schema[path];
			var fields = _members.Where(x => !(x.Value is SharpObject || x.Value is SharpObjectList))
				.OrderBy(x => x.Key).ToDictionary(x=>x.Key, x=>x.Value);

			// Check if the field set matches the current columns
			bool match = true;
			if (fields.Count != columns.Count)
			{
				match = false;
			}
			else
			{
				match = columns.All(x => fields.ContainsKey(x));
			}

			if (!match)
			{
				columns.Clear();
				if (required != null)
					columns.AddRange(required);
			}

			//WriteFlatConfigValueLine(writer, fields, columns);
			foreach (var name in fields.Keys)
				names.Add(name);
			

			foreach (var item in _members.Where(x => x.Value is SharpObjectList && ((SharpObjectList)x.Value).HasOnlyValue)
										 .OrderBy(x => x.Key))
			{
				if (names.Contains(item.Key)) continue;

				foreach (var value in (IEnumerable<object>)((SharpObjectList)item.Value).GetValueList())
				{
					var vt = new SharpValue(item.Value).Type;
					WriteConfigValueLine(writer, path, item.Key, item.Value, vt, null, true);
				}

				allowSkipNextNode = false;
			}

			foreach (var item in _members.Where(x => x.Value is SharpObjectList).OrderBy(x => x.Key))
			{
				if (names.Contains(item.Key)) continue;

				foreach (var obj in (item.Value as SharpObjectList))
				{
					obj.AppendFlatConfig(writer, String.Format("{0}/{1}", path, item.Key), schema, true);
				}
				allowSkipNextNode = false;
			}

			foreach (var item in _members.Where(x => !names.Contains(x.Key)).OrderBy(x => x.Key))
			{
				if (item.Value is SharpObject)
				{
					var obj = (SharpObject)item.Value;
					obj.AppendFlatConfig(writer, String.Format("{0}/{1}", path, item.Key), schema);

				}
				allowSkipNextNode = false;
			}

			return allowSkipNextNode;
		}
		
		public void AppendConfig(StreamWriter writer, string path, HashSet<string> schema = null, bool repeated = false, bool skipEmpty = false)
		{
			HashSet<string> names = new HashSet<string>();
			if (schema == null) schema = new HashSet<string>();

			if (!schema.Contains(path))
			{
				if (repeated)
					writer.WriteLine("{0}~Repeated", path);
				else 
					writer.WriteLine(path);
				schema.Add(path);	
			}
			else
			{
				writer.WriteLine(path);
			}

			foreach (var item in _members.Where(x => !(x.Value is SharpObject || x.Value is SharpObjectList))
										 .OrderBy(x=>x.Key))
			{
				if (names.Contains(item.Key)) continue;

				if (skipEmpty && (item.Value == null || item.Value is String && String.IsNullOrEmpty((string)item.Value)))
					continue;

				if (_types.ContainsKey(item.Key))
				{
					var vt = _types[item.Key];
					
					WriteConfigValueLine(writer, path, item.Key, item.Value, vt, schema);
				}
				else
				{
					var vt = new SharpValue(item.Value).Type;
					WriteConfigValueLine(writer, path, item.Key, item.Value, vt, schema);
				}
			}

			foreach (var item in _members.Where(x => x.Value is SharpObjectList && ((SharpObjectList)x.Value).HasOnlyValue)
										 .OrderBy(x=>x.Key))
			{
				if (names.Contains(item.Key)) continue;

				foreach (var value in (IEnumerable<object>)((SharpObjectList) item.Value).GetValueList())
				{
					var vt = new SharpValue(item.Value).Type;
					WriteConfigValueLine(writer, path, item.Key, item.Value, vt, schema, true);
				}
			}

			foreach (var item in _members.Where(x => x.Value is SharpObjectList).OrderBy(x => x.Key))
			{
				if (names.Contains(item.Key)) continue;

				foreach (var obj in (item.Value as SharpObjectList))
				{
					obj.AppendConfig(writer, String.Format("{0}/{1}", path, item.Key), schema, true);
				}
			}

			foreach (var item in _members.Where(x=>!names.Contains(x.Key)).OrderBy(x=>x.Key))
			{
				if (item.Value is SharpObject)
				{
					var obj = (SharpObject) item.Value;
					obj.AppendConfig(writer, String.Format("{0}/{1}", path, item.Key), schema);
				}
			}
		}
	}

	public static class SharpObjectExtension
	{
		public static string Format(this ISharpObjectFormat obj, string format, params object[] fields)
		{
			return SharpObject.Format(obj, format, fields);
		}
		
	}
}
 