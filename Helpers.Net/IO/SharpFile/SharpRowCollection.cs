using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpRowCollection
	{
		private readonly List<SharpRow> _rows = new List<SharpRow>();
		private readonly List<SharpRow> _insertRows = new List<SharpRow>();
		private readonly List<SharpRow> _deleteRows = new List<SharpRow>();
		private readonly Dictionary<SharpNodeRow, SharpObject> _updateRows = new Dictionary<SharpNodeRow, SharpObject>(); 

		private SharpNodeCollection _nodes = null;

		public void OnValueChanged(SharpNodeRow row, int column, object value)
		{
			_updateRows[row] = null;
		}

		internal void OnMemberChanged(SharpNodeRow row, SharpObject source, string fieldname, object value)
		{
			_updateRows[row] = source;
		}

		#region Add Object

		private void AddObjectMember(string memberPath, string key, object value, SharpNode node,  List<SharpNodeRow> rows)
		{
			var objectList = value as SharpObjectList;
			if (objectList != null)
			{
				if (objectList.HasOnlyValue)
				{
		
					AddObjectMember(memberPath, "", objectList.GetValueList(), node, rows);
					return;
				}
			}

			var memberList = value as IEnumerable<IDictionary<string, object>>;
			if (memberList != null)
			{
				var memberListNode = node ?? _nodes.GetNode(memberPath);
				if (memberListNode == null)
				{
					memberListNode = new SharpNode
					{
						Path = memberPath,
						Name = SharpNode.GetNodeName(memberPath),
						NodeType = SharpNodeType.Repeated
					};
					_nodes.Add(memberListNode);
				}

				foreach (var item in memberList)
				{
					AddObject(memberPath, item, rows);
				}

				return;
			}

			var memberObj = value as IDictionary<string, object>;
			if (memberObj != null)
			{
				AddObject(memberPath, memberObj, rows);
				return;
			}

			// Value Row
			if (!key.StartsWith("@") && !memberPath.EndsWith("/#"))
				memberPath += "/#";

			// Multiple Value Row
			SharpValueType memberValueType;
			if (SharpValue.TryGetValueList(value, out memberValueType))
			{
				var memberValueListNode = _nodes.GetNode(memberPath);
				if (memberValueListNode == null)
				{
					memberValueListNode = new SharpNode
					{
						Path = memberPath,
						Name = SharpNode.GetNodeName(memberPath),
						IsValueNode = true,
						ValueType = memberValueType,
						NodeType = SharpNodeType.Repeated
					};
					_nodes.Add(memberValueListNode);
				}

				foreach (var val in ((IEnumerable)value))
				{
					rows.Add(new SharpValueRow(memberValueListNode, val));
				}

				return;
			}

			// Single Value Row
			var memberValue = new SharpValue(value);

			var memberNode = _nodes.GetNode(memberPath);
			if (memberNode == null)
			{
				memberNode = new SharpNode()
				{
					Path = memberPath,
					Name = SharpNode.GetNodeName(memberPath),
					IsValueNode = true,
					ValueType = memberValue.Type

				};
				_nodes.Add(memberNode);
			}

			rows.Add(new SharpValueRow(memberNode, memberValue.Value));	
		}

		private void AddObject(string nodePath, IDictionary<string, object> item, List<SharpNodeRow> rows)
		{
			var node = _nodes.GetNode(nodePath);
			if (node == null)
			{
				node = new SharpNode
				{
					Path = nodePath,
					Name = SharpNode.GetNodeName(nodePath)
				};
				_nodes.Add(node);
			}

			rows.Add(new SharpNodeRow(node));

			// Object members are written to rows in alphabetical order in the following groups:
			//   1. Attributes
			//   2. Value Members
			//   3. Object Members
			//   4. Final Object Member (Many / Only / Last)
			
			List<string> fieldKeys = new List<string>();
			List<string> objKeys = new List<string>();

			foreach (var key in item.Keys)
			{
				if (key.Length > 0 && key[0] == '@')
				{
					var memberPath = string.Format("{0}/{1}", nodePath, key);
					AddObjectMember(memberPath, key, item[key], null, rows);
				}
				else if (item[key] is IDictionary<string, object> || item[key] is IEnumerable<IDictionary<string, object>>)
				{
					objKeys.Add(key);
				}
				else
				{
					fieldKeys.Add(key);
				}
			}

			foreach (var key in fieldKeys)
			{
				var memberPath = string.Format("{0}/{1}", nodePath, key);
				AddObjectMember(memberPath, key, item[key], null, rows);	
			}

			string lastKey = null;
			foreach (var key in objKeys)
			{
				var memberPath = string.Format("{0}/{1}", nodePath, key);
				var memberNode = _nodes.GetNode(memberPath);
				if (lastKey == null && memberNode != null && (memberNode.NodeType == SharpNodeType.Many || memberNode.NodeType == SharpNodeType.Only))
				{
					lastKey = key;
					continue;
				}
				AddObjectMember(memberPath, key, item[key], memberNode, rows);	
			}

			if (lastKey != null)
			{
				var memberPath = string.Format("{0}/{1}", nodePath, lastKey);
				AddObjectMember(lastKey, memberPath, item[lastKey], null, rows);
			}
		}

		public SharpNodeRow AddRange(string nodePath, IEnumerable<ISharpObject> items, bool collapseLeafNodes = true)
		{
			var rows = new List<SharpNodeRow>();

			foreach (var item in items)
				AddObject(nodePath, item.GetObject(), rows);

			var topNode = _nodes.GetNode(nodePath).Parent;
			var topRow = new SharpNodeRow(topNode);

			UpdateRowLinks(topRow, rows.GetEnumerator(), collapseLeafNodes);

			Add(topRow);
			return topRow;
		}

		public SharpNodeRow Add(string nodePath, IDictionary<string, object> item, bool collapseLeafNodes=true)
		{
			var rows = new List<SharpNodeRow>();
			AddObject(nodePath, item, rows);

			var row = rows[0];
			UpdateRowLinks(row, rows.Skip(1).GetEnumerator(), collapseLeafNodes);

			Add(row);

			return row;
		}
	
		#endregion

		public virtual SharpNodeRow GetRoot()
		{

			foreach (var row in _rows)
			{
				if (row is SharpNodeRow)
					return (SharpNodeRow) row;
			}
		
			return null;
		}

		public void Add(SharpRow row)
		{
			row.Collection = this;
			_insertRows.Add(row);
		}

		public void AddRange(IEnumerable<SharpRow> rows, bool collapseLeafNodes = true, bool insertParentNodes=false, bool removeComments=false, bool skipFirstParentRow=false)
		{
			if (removeComments)
				rows = RemoveComments(rows);

			if (insertParentNodes)
				rows = InsertParentRows(rows, skipFirstParentRow:skipFirstParentRow);

			InsertRows(rows.GetEnumerator(), collapseLeafNodes);
		}

		#region Dynamically Add / Remove Rows 
		
		public IEnumerable<SharpRow> RemoveComments(IEnumerable<SharpRow> rows)
		{
			foreach (var row in rows)
			{
				if (!(row is SharpMetaCommentRow))
					yield return row;
			}
		}

		public IEnumerable<SharpRow> InsertParentRows(IEnumerable<SharpRow> rows, bool skipFirstParentRow = false)
		{
			SharpNodeStack stack = new SharpNodeStack();

			foreach (var row in rows)
			{
				var nodeRow = row as SharpNodeRow;
				if (nodeRow == null)
				{
					yield return row;
					continue;
				}

				if (skipFirstParentRow)
				{
					stack.Enqueue(nodeRow.Node);
					skipFirstParentRow = false;
					yield return row;
					continue;
				}

				if (stack.Top == nodeRow.Node.Parent)
				{
					if (!nodeRow.Node.IsValueNode)
						stack.Enqueue(nodeRow.Node);

					yield return row;
					continue;
				}

				while (stack.Top != null && !stack.Top.HasDescendant(nodeRow.Node))
					stack.Dequeue();

				if (stack.Top == null || stack.Top.HasDescendant(nodeRow.Node))
				{
					var nodes = nodeRow.Node.PathTo(stack.Top);
					foreach (var node in nodes)
					{
						stack.Enqueue(node);
						yield return new SharpNodeRow(node);
					}

					if (!nodeRow.Node.IsValueNode)
						stack.Enqueue(nodeRow.Node);

					yield return row;
				}
			}
		}
		#endregion

		protected IEnumerable<SharpRow> DequeueInsertRows()
		{
			var rows = new List<SharpRow>(_insertRows);
			_insertRows.Clear();
			return rows;
		}

		public void InsertRows(IEnumerator<SharpRow> rows, bool collapseLeafNodes = true, SharpNodeRow rootRow = null, SharpNodeRow prevRow = null)
		{
			bool firstRow = true;
			bool autoLink = prevRow != null || rootRow != null;

			while (rows.MoveNext())
			{
				rows.Current.Collection = this;
				var nodeRow = rows.Current as SharpNodeRow;
				if (nodeRow == null)
				{
					_insertRows.Add(rows.Current);
					continue;
				}

				while (nodeRow != null)
				{
					if (prevRow != null)
					{
						if (prevRow.Node.Parent == nodeRow.Node.Parent)
						{
							nodeRow.Prev = prevRow;
							if (!firstRow)
							{
								prevRow.Next = nodeRow;
								nodeRow.Root = prevRow.Root;
							}

							prevRow = nodeRow;
						}
						else
						{
							prevRow = null;
							firstRow = true;
						}
					}
					else if (rootRow != null)
					{
						if (nodeRow.Node.Parent == rootRow.Node)
						{
							nodeRow.Root = rootRow;
							firstRow = true;
							prevRow = nodeRow;
						}
					}

					if (!autoLink || nodeRow.Prev == null || firstRow)
					{
						_insertRows.Add(nodeRow);
						firstRow = false;
					}

					if (!UpdateRowLinks(nodeRow, rows, collapseLeafNodes))
						break;

					nodeRow = rows.Current as SharpNodeRow;
					if (nodeRow == null)
					{
						_insertRows.Add(nodeRow);
					}

				}
			}
		}

		public int Count
		{
			get { return _rows.Count; }
		}

		public IEnumerable<SharpRow> GetTopRows()
		{
			foreach (var row in _rows)
			{
				var nodeRow = row as SharpNodeRow;
				while (nodeRow != null)
				{
					yield return nodeRow;
					nodeRow = nodeRow.Next;
				}
			}
		}

		public virtual IEnumerable<SharpRow> GetRows(SharpNode node=null)
		{
			var stack = new List<bool>();

			foreach (var row in _rows)
			{
				var nodeRow = row as SharpNodeRow;
				if (nodeRow != null)
				{
					var top = nodeRow;
					var stackPos = 0;
					stack.Add(false);

					while (top != null)
					{
						bool processNode = true;

						if (!stack[stackPos])
						{
							if (node == null || node == top.Node)
								yield return top;

							// Short circuit any branches past the selected node or any branches that don't
							// contain the selected node.
							if (node != null && (node == top.Node || !top.Node.HasDescendant(node)))
								processNode = false;
						}

						if (!stack[stackPos] && top.First != null && processNode)
						{
							stack[stackPos] = true;
							stackPos++;
							if (stackPos >= stack.Count)
								stack.Add(false);
							else
								stack[stackPos] = false;
							top = top.First;
						}
						else if (top.Next != null)
						{
							stack[stackPos] = false;
							top = top.Next;
						}
						else if (top == nodeRow)
						{
							top = null;
						}
						else
						{
							stackPos--;
							top = top.Root;
						}
					}

				}
				else
				{
					if (node == null)
						yield return row;	
				}
			}
		}

		public bool TryCollapseLeafNode(SharpNodeRow row)
		{
			List<SharpValueRow> columns = new List<SharpValueRow>();
			SharpNodeRow prev = row;
			while (prev is SharpValueRow && prev.Node.IsSingleValueNode)
			{
				columns.Insert(0, prev as SharpValueRow);
				prev = prev.Prev;
			}

			if (columns.Count > 2)
			{
				string mapPath = string.Join(",", columns.Select(r => r.Node.Index));
				var mapNode = _nodes.GetNode(mapPath) as SharpNodeMap;
				if (mapNode == null)
				{
					mapNode = new SharpNodeMap(columns);
					mapNode.Path = mapPath;
					mapNode.IsValueNode = true;
					mapNode.MapType = SharpMapType.Sequence;
					_nodes.Add(mapNode);
				}

				var mapRow = new SharpValueRow(mapNode, columns.Select(c => c.Values[0]));
				mapRow.Collection = this;
				mapRow.Next = row.Next;
				mapRow.Root = row.Root;
				if (prev != null)
				{
					mapRow.Prev = prev;
					prev.Next = mapRow;
				}
				else if (mapRow.Root != null)
				{
					mapRow.Root.First = mapRow;
				}

				return true;
			}

			return false;
		}

		public bool UpdateRowLinks(SharpNodeRow root, IEnumerator<SharpRow> rows, bool collapseLeafNodes = true)
		{
			SharpNodeRow top = root;

			bool skipCollapseCheck = false;

			while (rows.MoveNext())
			{
				rows.Current.Collection = this;

				var nodeRow = rows.Current as SharpNodeRow;
				if (nodeRow == null)
					return true;

				if (!root.Node.HasDescendant(nodeRow.Node))
					return true;

				bool done = false;
				while (!done)
				{
					top.Collection = this;

					if (nodeRow.Node.Parent == top.Node)
					{
						if (top.First == null)
							top.First = nodeRow;

						nodeRow.Root = top;
						top = nodeRow;
						done = true;
						skipCollapseCheck = true;
						break;
					}

					while (nodeRow.Node.Parent != top.Node.Parent)
					{
						if (top.Root == null)
							return true;

						top = top.Root;
					}

					if (nodeRow.Node.Parent == top.Node.Parent)
					{
						top.Next = nodeRow;
						nodeRow.Prev = top;
						nodeRow.Root = top.Root;
						top = nodeRow;
						done = true;

						if (collapseLeafNodes && !skipCollapseCheck && !top.Node.IsSingleValueNode && top.Prev is SharpValueRow && top.Prev.Node.IsSingleValueNode)
						{
							TryCollapseLeafNode(top.Prev);
							skipCollapseCheck = true;
						}
						else
						{
							skipCollapseCheck = false;
						}
					}

					if (!done)
					{

						if (top == root)
							return true;

						if (collapseLeafNodes && !skipCollapseCheck && top.Node.IsSingleValueNode && top is SharpValueRow)
						{
							TryCollapseLeafNode(top);
						}
						
						top = top.Root;
						skipCollapseCheck = false;
					}
				}
			}

			if (collapseLeafNodes && !skipCollapseCheck && top.Node.IsSingleValueNode && top is SharpValueRow)
			{
				TryCollapseLeafNode(top);
			}

			return false;
		}


		public SharpNodeCollection Nodes {
			get { return _nodes; }
			set { _nodes = value; }
		}

		public void Clear()
		{
			_rows.Clear();
			_insertRows.Clear();
			_deleteRows.Clear();
		}


		internal void SubmitChanges()
		{
			foreach (var update in _updateRows)
			{
				if (update.Value == null)
				{
					update.Key.ApplyChanges();
				}
				else
				{
					var originalRow = update.Key;

					var rows = new List<SharpNodeRow>();
					AddObject(update.Key.Node.Path, update.Value, rows);

					var newRow = rows[0];
					originalRow.First = null;
					UpdateRowLinks(originalRow, rows.Skip(1).GetEnumerator());
				}
			}
				
			
			_updateRows.Clear();

			List<SharpNodeRow> unlinkRows = new List<SharpNodeRow>();

			foreach (var row in _deleteRows)
			{
				bool removed = false;
				if (_insertRows.Contains(row))
				{
					_insertRows.Remove(row);
					removed = true;
				}
				if (_rows.Contains(row))
				{
					_rows.Remove(row);
					removed = true;
				}
				
				if (!removed && row is SharpNodeRow)
					unlinkRows.Add(row as SharpNodeRow);
			}
			
			_deleteRows.Clear();

			foreach (var row in _insertRows)
			{
				var nodeRow = row as SharpNodeRow;
				if (nodeRow == null)
				{
					_rows.Add(row);
					continue;
				}

				if (nodeRow.Prev == null && nodeRow.Root == null && nodeRow.Next == null)
				{
					_rows.Add(row);
					continue;
				}

				if (nodeRow.Prev != null && nodeRow.Root == null)
				{
					// Insert new row after the previous node
					nodeRow.Root = nodeRow.Prev.Root;
					nodeRow.Next = nodeRow.Prev.Next;
					nodeRow.Prev.Next = nodeRow;
					if (nodeRow.Root.First == null)
						nodeRow.Root.First = nodeRow;

				}
				else if (nodeRow.Next != null && nodeRow.Root == null)
				{
					// Insert new row before the previous node
					nodeRow.Root = nodeRow.Next.Root;
					nodeRow.Prev = nodeRow.Next.Prev;
					nodeRow.Next.Prev = nodeRow;
					if (nodeRow.First == null || nodeRow.First == nodeRow.Next)
					{
						nodeRow.First = nodeRow;
					}
				}
				else
				{
					if (nodeRow.Root.First == null)
					{
						// Insert new row as first child of root
						nodeRow.Root.First = nodeRow;
					}
					else
					{
						// Insert new row as last child of root
						var last = nodeRow.Root.First;
						while (last.Next != null)
							last = last.Next;

						last.Next = nodeRow;
						nodeRow.Prev = last;
					}
				}
			}

			_insertRows.Clear();

			foreach (var nodeRow in unlinkRows)
			{
				if (nodeRow.Prev != null && nodeRow.Prev.Next == nodeRow)
					nodeRow.Prev.Next = nodeRow.Next;

				if (nodeRow.Next != null && nodeRow.Next.Prev == nodeRow)
					nodeRow.Next.Prev = nodeRow.Prev;

				if (nodeRow.Root != null && nodeRow.Root.First == nodeRow)
				{
					nodeRow.Root.First = nodeRow.Next ?? nodeRow.Prev;
				}
			}

		}

		internal void DiscardPendingChanges()
		{
			_insertRows.Clear();
			_deleteRows.Clear();
		}

		public void Remove(SharpRow row)
		{
			_deleteRows.Add(row);
		}


	}
}
