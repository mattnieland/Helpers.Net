using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;

namespace Helpers.Net.IO.SharpFile
{
    public class SharpObjectDynamic : DynamicObject, IDictionary<string, object>
	{
		private readonly SharpObject _obj;

		internal SharpObjectDynamic(SharpObject obj)
		{
			_obj = obj;
		}

		public SharpObject GetObject()
		{
			return _obj;
		}

		#region DynamicObject
		
		public override bool TryConvert(ConvertBinder binder, out object result)
		{
			if (binder.Type == typeof(Dictionary<string, object>))
			{
				result = _obj.ToDictionary();
				return true;
			}

			return base.TryConvert(binder, out result);
		}

		public override bool TrySetMember(SetMemberBinder binder, object value)
		{
			_obj[binder.Name] = value;
			return true;
		}
		
		public override bool TryGetMember(GetMemberBinder binder, out object result)
		{
			result = null;
			if (!_obj.TryGetValue(binder.Name, out result))
				return false;

			if (result is SharpObjectList)
			{
				result = ((SharpObjectList) result).AsDynamic();
			}
			else if (result is SharpObject)
			{
				result = ((SharpObject)result).AsDynamic();
			}

			return true;
		}

		public bool HasOnlyValue
		{
			get { return _obj.HasOnlyValue; }
		}

		public bool HasValue
		{
			get { return _obj.HasValue;  }
		}

		public object GetValue()
		{
			return _obj.GetValue();
		}

		public Dictionary<string, object> ToDictionary()
		{
			return _obj.ToDictionary();
		}
		
		#endregion

		#region IDictionary
		
		public IEnumerator<KeyValuePair<string, dynamic>> GetEnumerator()
		{
			foreach (var item in _obj)
			{
				if (item.Value is SharpObject)
					yield return new KeyValuePair<string, dynamic>(item.Key, ((SharpObject)item.Value).AsDynamic());
				else if (item.Value is SharpObjectList)
					yield return new KeyValuePair<string, dynamic>(item.Key, ((SharpObjectList) item.Value).AsDynamic());
				else
					yield return item;
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable) _obj).GetEnumerator();
		}

		public void Add(KeyValuePair<string, object> item)
		{
			if (item.Value is SharpObjectDynamic)
			{
				_obj.Add(new KeyValuePair<string, object>(item.Key, ((SharpObjectDynamic)item.Value).GetObject()));
			}
			else if (item.Value is SharpObjectListDynamic)
			{
				_obj.Add(new KeyValuePair<string, object>(item.Key, ((SharpObjectListDynamic)item.Value).GetObjectList()));
			}
			else
			{
				_obj.Add(item);
			}
		}

		public void Clear()
		{
			_obj.Clear();
		}

		public bool Contains(KeyValuePair<string, object> item)
		{
			if (item.Value is SharpObjectDynamic)
			{
				return _obj.Contains(new KeyValuePair<string, object>(item.Key, ((SharpObjectDynamic)item.Value).GetObject()));
			}
			else if (item.Value is SharpObjectListDynamic)
			{
				return _obj.Contains(new KeyValuePair<string, object>(item.Key, ((SharpObjectListDynamic)item.Value).GetObjectList()));
			}
			else
			{
				return _obj.Contains(item);
			}
		}

		public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
		{
			_obj.CopyTo(array, arrayIndex);
		}

		public bool Remove(KeyValuePair<string, object> item)
		{
			if (item.Value is SharpObjectDynamic)
			{
				return _obj.Remove(new KeyValuePair<string, object>(item.Key, ((SharpObjectDynamic)item.Value).GetObject()));
			}
			else if (item.Value is SharpObjectListDynamic)
			{
				return _obj.Remove(new KeyValuePair<string, object>(item.Key, ((SharpObjectListDynamic)item.Value).GetObjectList()));
			}
			else
			{
				return _obj.Remove(item);
			}
		}

		public int Count
		{
			get { return _obj.Count; }
		}

		public bool IsReadOnly
		{
			get { return _obj.IsReadOnly; }
		}

		public bool ContainsKey(string key)
		{
			return _obj.ContainsKey(key);
		}

		public void Add(string key, object value)
		{
			if (value is SharpObjectDynamic)
			{
				_obj.Add(key, ((SharpObjectDynamic)value).GetObject());
			}
			else if (value is SharpObjectListDynamic)
			{
				_obj.Add(key, ((SharpObjectListDynamic)value).GetObjectList());				
			}
			else
			{
				_obj.Add(key, value);	
			}
		}

		public bool Remove(string key)
		{
			return _obj.Remove(key);
		}

		public bool TryGetValue(string key, out dynamic value)
		{
			
			object result;
			if (!_obj.TryGetValue(key, out result))
			{
				value = null;
				return false;
			}

			if (result is SharpObject)
			{
				value = ((SharpObject) result).AsDynamic();
			}
			else if (result is SharpObjectList)
			{
				value = ((SharpObjectList) result).AsDynamic();
			}
			else
			{
				value = result;
			}

			return true;
		}

		public dynamic this[string key]
		{
			get
			{
				dynamic result;
				TryGetValue(key, out result);
				return result;
			}

			set
			{
				if (value is SharpObjectDynamic)
				{
					_obj[key] = ((SharpObjectDynamic) value).GetObject();
				}
				else if (value is SharpObjectListDynamic)
				{
					_obj[key] = ((SharpObjectListDynamic) value).GetObjectList();
				}
				else
				{
					_obj[key] = value;
				}
			}
		}

		public ICollection<string> Keys
		{
			get { return _obj.Keys; }
		}

		public ICollection<dynamic> Values
		{
			get
			{
				List<dynamic> result = new List<dynamic>();

				foreach (var value in _obj.Values)
				{
					if (value is SharpObject)
					{
						result.Add(((SharpObject) value).AsDynamic());
					}
					else if (value is SharpObjectList)
					{
						result.Add( ((SharpObjectList)value).AsDynamic() );
					}
					else
					{
						result.Add(value);
					}
				}

				return result;
			}
		}

		#endregion
	}

	public class SharpObjectListDynamic : DynamicObject, IList<object>
	{
		private readonly SharpObjectList _list;
		
		internal SharpObjectListDynamic(SharpObjectList list)
		{
			_list = list;
		}

		public bool HasOnlyValue
		{
			get { return _list.HasOnlyValue; }

		}
		
		public SharpObjectList GetObjectList()
		{
			return _list;
		}

		public IEnumerable<dynamic> AsEnumerable()
		{
			foreach (var obj in _list)
			{
				yield return obj.AsDynamic();
			}
		}

		#region LINQ Emulation
		
		public dynamic First()
		{
			return _list.First().AsDynamic();
		}

		public dynamic FirstOrDefault()
		{
			var obj = _list.FirstOrDefault();
			return obj != null ? obj.AsDynamic() : null;
		}

		public IEnumerable<dynamic> Where(Func<dynamic, bool> selector)
		{
			return _list.AsDynamicEnumerable().Where(selector);
		}

		#endregion

		#region IEnumerable

		public IEnumerator<dynamic> GetEnumerator()
		{
			foreach (var obj in _list)
			{
				if (obj != null)
					yield return obj.AsDynamic();
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion

		#region IList
		
		public int IndexOf(object item)
		{
			if (item is SharpObject)
			{
				return _list.IndexOf(item as SharpObject);
			}
			else if (item is SharpObjectDynamic)
			{
				return _list.IndexOf(((SharpObjectDynamic) item).GetObject());
			}
			return -1;
		}

		public void Insert(int index, object item)
		{
			if (item is SharpObject)
			{
				_list.Insert(index, item as SharpObject);
			}
			else if (item is SharpObjectDynamic)
			{
				_list.Insert(index, ((SharpObjectDynamic)item).GetObject());
			}
		}

		public void RemoveAt(int index)
		{
			_list.RemoveAt(index);
		}

		public dynamic this[int index]
		{
			get
			{
				var obj = _list[index];
				return obj != null ? obj.AsDynamic() : null;
			}
			set
			{
				if (value is SharpObject)
				{
					_list[index] = value as SharpObject;
				}
				else if (value is SharpObjectDynamic)
				{
					_list[index] = ((SharpObjectDynamic)value).GetObject();
				}
			}
		}

		public void Add(object item)
		{
			if (item is SharpObject)
			{
				_list.Add(item as SharpObject);
			}
			else if (item is SharpObjectDynamic)
			{
				_list.Add(((SharpObjectDynamic)item).GetObject());
			}
		}

		public void AddRange(IEnumerable<object> items )
		{
			foreach (var item in items)
				Add(item);
		}

		public void Clear()
		{
			_list.Clear();
		}

		public bool Contains(object item)
		{
			if (item is SharpObject)
			{
				return _list.Contains(item as SharpObject);
			}
			else if (item is SharpObjectDynamic)
			{
				return _list.Contains(((SharpObjectDynamic) item).GetObject());
			}

			return false;
		}

		public void CopyTo(object[] array, int arrayIndex)
		{
			foreach (var item in _list)
			{
				array[arrayIndex++] = item.AsDynamic();
			}
		}

		public bool Remove(object item)
		{
			if (item is SharpObject)
			{
				return _list.Remove(item as SharpObject);
			}
			else if (item is SharpObjectDynamic)
			{
				return _list.Remove(((SharpObjectDynamic)item).GetObject());
			}
			return false;
		}

		public int Count { get { return _list.Count; } }
		public bool IsReadOnly {get { return _list.IsReadOnly; }}

		#endregion
	}

}
