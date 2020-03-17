using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Helpers.Net.Objects
{
	public class SharpObjectList : IList<SharpObject>
	{
		private bool _useCache = false;
		private bool _isExpanded = false;
		private readonly object _sync = new Object();
		private readonly SharpRowCursor _source;
		private readonly List<SharpObject> _items = new List<SharpObject>();

		private SharpObject _parent;
		private string _name = "";

		private SharpNode _node = null;
		private SharpObjectListDynamic _wrapper;

		public SharpObjectList(SharpObject parent, string name)
		{
			_parent = parent;
			_name = name;
		}

		internal SharpObjectList(SharpObject parent, string name, IEnumerable<ISharpObject> items)
		{
			_parent = parent;
			_name = name;
			_items = items.Select(x => x.GetObject()).ToList();
			_useCache = true;
			_isExpanded = true;
		}

		internal SharpObjectList(SharpObject parent, string name, SharpRowCursor source, SharpNode node=null, bool useCache = false)
		{
			_parent = parent;
			_name = name;
			_source = source;
			_useCache = useCache;
			_node = node;
		}

		public dynamic AsDynamic()
		{
			if (_wrapper == null)
				_wrapper = new SharpObjectListDynamic(this);

			return _wrapper;
		}

		public IEnumerable<dynamic> AsDynamicEnumerable()
		{
			foreach (SharpObject obj in this)
			{
				if (obj != null)
					yield return obj.AsDynamic();
			}
		}
		
		public bool HasOnlyValue
		{
			get { return _items.TrueForAll(x => x.HasOnlyValue); }
		}

		public object GetValueList()
		{
			SharpValueType listType = SharpValueType.None;
			List<SharpValueType> types = _items.Select(x => x.GetType("#")).Distinct().ToList();
			if (types.Count == 1)
			{
				listType = types[0];
			}

			return SharpValue.AsValueList(_items.Select(x => x.GetValue()), listType);
		}

		public virtual void OnContentChanged()
		{
			if (_parent != null && _parent.IsExpanded)
			{
				_parent.OnMemberChanged(_name, _items);
			}
		}

		#region IList

		public int Count
		{
			get
			{
				return _items.Count;
			}
		}
	
		public bool IsReadOnly
		{
			get { return false; }
		}

		public bool Contains(SharpObject item)
		{
			return _items.Contains(item);
		}

		public int IndexOf(SharpObject item)
		{
			return _items.IndexOf(item);
		}

		public SharpObject this[string path]
		{
			get { return null; }
			set { }
		}

		public SharpObject this[int index]
		{
			get
			{
				if (_items.Count == 0)
					return null;

				if (index < 0)
					index += _items.Count;

				if (index >= 0 && index < _items.Count)
					return _items[index];

				return null;
			}
			set
			{
				lock (_sync)
				{
					_useCache = true;
					if (index < 0)
						index += _items.Count;

					if (index > 0 && index < _items.Count)
					{
						_items[index] = value;
					}
					else
					{
						if (index < 0)
						{
							_items.Insert(0, value);
						}
						else
						{
							if (index > 0)
							{
								while (_items.Count < index - 1)
								{
									_items.Add(null);
								}
							}

							_items.Add(value);
						}
					}

					OnContentChanged();
				}
			}
		}
	
		public void Add(SharpObject obj)
		{
			lock (_sync)
			{
				_useCache = true;
				_items.Add(obj);
				OnContentChanged();
			}
		}

		public void AddRange(IEnumerable<SharpObject> objs)
		{
			lock (_sync)
			{
				_useCache = true;
				_items.AddRange(objs);
				OnContentChanged();
			}
		}

		public void Insert(int index, SharpObject item)
		{
			lock (_sync)
			{
				_items.Insert(index, item);
				OnContentChanged();
			}
		}

		public bool Remove(SharpObject item)
		{
			lock (_sync)
			{
				OnContentChanged();
				return _items.Remove(item);
			}
		}

		public void RemoveAt(int index)
		{
			lock (_sync)
			{
				OnContentChanged();
				_items.RemoveAt(index);
			}
		}

		public void Clear()
		{
			lock (_sync)
			{
				OnContentChanged();
				_items.Clear();
			}
		}

		public void CopyTo(SharpObject[] array, int arrayIndex)
		{
			lock (_sync)
			{
				OnContentChanged();
				_items.CopyTo(array, arrayIndex);
			}
		}
		
		#endregion

		#region IEnumerable
		public IEnumerator<SharpObject> GetEnumerator()
		{
			bool expandSource = false;
			lock (_sync)
			{
				expandSource = !_isExpanded && _source != null;
			}

			if (_useCache)
			{
				int length = _items.Count;
				for (int i = 0; i < length; i++)
				{
					if (_items[i] != null)
						yield return _items[i];
				}
			}
			
			if (expandSource)
			{
				List<SharpObject> newItems = new List<SharpObject>();

				// Copy the cursor so this can be called by multiple threads
				var cursor = new SharpRowCursor(_source);
				cursor.Reset();
				
				while (cursor.MoveNext())
				{
					if (_node != null && cursor.Current.Node != _node)
						break;

					var obj = new SharpObject(cursor.Current);
					if (_useCache)
						newItems.Add(obj);

					yield return obj;
				}

				lock (_sync)
				{
					if (!_isExpanded && _useCache)
					{
						_items.AddRange(newItems);
						_isExpanded = true;
					}
				}
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
		#endregion

		#region IDisposable
		public void Dispose()
		{
			_items.Clear();
		}
		#endregion
		
	}
}