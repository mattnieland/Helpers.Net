using Helpers.Net.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpRow
	{
		public SharpRowType RowType = SharpRowType.None;
		public SharpRowCollection Collection = null;
	}

	#region Node Rows
	
	internal class SharpNodeRow : SharpRow
	{
		public SharpNode Node;

		public SharpNodeRow Root = null;			// Parent
		public SharpNodeRow Prev = null;			// Previous Sibling
		public SharpNodeRow Next = null;			// Next Sibling
		public SharpNodeRow First = null;		// First Child
		
		public Dictionary<string, KeyValuePair<string, string>> QueryPath = new Dictionary<string, KeyValuePair<string, string>>();

		public SharpNodeRow() {RowType = SharpRowType.Node;}

		public SharpNodeRow(SharpNode node)
		{
			RowType = SharpRowType.Node;
			Node = node;
		}

		public override string ToString()
		{
			return Node.Path;
		}

		public virtual void OnValueChanged(int column, object value)
		{
			if (Collection != null)
			{
				Collection.OnValueChanged(this, column, value);
			}
		}

		public virtual void OnMemberChanged(SharpObject source, string fieldName, object value)
		{
			if (Collection != null)
			{
				Collection.OnMemberChanged(this, source, fieldName, value);
			}
		}

		public virtual void ApplyChanges()
		{
			
		}

		public virtual void DiscardChanges()
		{
			
		}

	}
	
	#endregion

	#region Value Rows
	
	internal class SharpValueRow : SharpNodeRow
	{
		public List<object> Values = new List<object>();
		public SharpValueRow() {RowType = SharpRowType.Value;}

		public SharpValueRow(SharpNode node, object value)
		{
			RowType = SharpRowType.Value;
			Node = node;
			Values.Add(value);
		}

		public SharpValueRow(SharpNode node, IEnumerable<object> values)
		{
			RowType = SharpRowType.Value;
			Node = node;
			if (values == null)
				Values.Add(null);
			else
				Values.AddRange(values);
		}

		public override void ApplyChanges()
		{
			if (Node.IsSingleValueNode)
			{
				if (Values.Count == 2)
				{
					Values[0] = Values[1];
					Values.RemoveAt(1);
				}
			}
			else
			{
				var updates = GetUpdateContainer();
				var mapNode = Node as SharpNodeMap;
				if (mapNode != null)
				{
					if (mapNode.MapType == SharpMapType.Fixed)
					{
						StringBuilder sb = Values[0] is StringBuilder
							? Values[0] as StringBuilder
							: new StringBuilder(Values[0] as string);

						foreach (var item in updates)
						{
							mapNode.UpdateColumnValue(item.Key, item.Value, sb);
						}
					}
					else if (mapNode.MapType == SharpMapType.Sequence)
					{
						foreach (var item in updates)
						{
							Values[item.Key] = item.Value;
						}
					}
				}

				updates.Clear();
			}
		}

		private Dictionary<int, object> GetUpdateContainer()
		{
			var mapNode = Node as SharpNodeMap;
			if (mapNode != null)
			{
				switch (mapNode.MapType)
				{
					case SharpMapType.Fixed:
						{
							if (Values.Count == 0)
								Values.Add(null);

							if (Values.Count == 1)
								Values.Add(new Dictionary<int, object>());

							return Values[1] as Dictionary<int, object>;
							
						}
						
					case SharpMapType.Sequence:
						{
							while (Values.Count < mapNode.GetColumnCount())
								Values.Add(null);
							if (Values.Count == mapNode.GetColumnCount())
								Values.Add(new Dictionary<int, object>());

							return Values[mapNode.GetColumnCount()] as Dictionary<int, object>;
						}
						
				}
			}
			
			return new Dictionary<int, object>();
		}

		public override void DiscardChanges()
		{
			if (Node.IsSingleValueNode)
			{
				if (Values.Count == 2)
					Values.RemoveAt(1);
			}
			else
			{
				GetUpdateContainer().Clear();
			}
		}
		
		public override void OnValueChanged(int column, object value)
		{
			if (Node.IsSingleValueNode)
			{
				if (Values.Count < 2)
				{
					while (Values.Count < 2)
						Values.Add(value);
				}
				else
				{
					Values[1] = value;	
				}
			}
			else
			{
				GetUpdateContainer()[column] = value;
			}

			base.OnValueChanged(column, value);
		}

		public IEnumerable<SharpRow> GetRows()
		{
			return Node.GetRows(Values);
		}

		public override string ToString()
		{
			if (Node.IsSingleValueNode)
			{
				return string.Format("{0}/{1} = {2}", Node.GetParentPath(), Node.Name, Values[0]);
			}

			return base.ToString();
		}

		public object GetColumnValue(int column)
		{
			var mapNode = Node as SharpNodeMap;
			if (mapNode != null)
			{
				if (mapNode.MapType == SharpMapType.Fixed)
				{
					return mapNode.GetColumnValue(column, Values[0] as string);
				}
				else if (mapNode.MapType == SharpMapType.Sequence)
				{
					return mapNode.GetColumnValue(column, Values);
				}
				else if (mapNode.MapType == SharpMapType.Variable)
				{
					if (Values.Count == 1 && Values[0] is string)
					{
						string line = (string)Values[0];
						Values.Clear();
						Values.AddRange(mapNode.ExpandRow(line).Select(x=>x.Value));
					}

					if (column < Values.Count)
						return Values[column];
				}
			}
			return null;
		}
	}

	#endregion

	#region Meta Rows

	internal class SharpMetaRow : SharpRow
	{
		public SharpMetaType MetaType = SharpMetaType.None;
	}

	internal class SharpMetaCommentRow : SharpMetaRow
	{
		public string Value;

		public override string ToString()
		{
			return String.Format("#{0}", Value);
		}
	}
	
	internal class SharpMetaNodeRow : SharpMetaRow
	{
		public int Index=-1;
		public string Path="";
		public SharpNodeType NodeType = SharpNodeType.Any;
		public SharpValueType ValueType = SharpValueType.None;
		public Dictionary<string, SharpValue> AdditionalInfo = new Dictionary<string, SharpValue>();
		public Dictionary<string, KeyValuePair<string, string>> QueryPath = new Dictionary<string, KeyValuePair<string, string>>();

		public SharpMetaNodeRow()
		{
			MetaType = SharpMetaType.Node;
		}

		public SharpMetaNodeRow(string row, string defaultPath="")
		{
			MetaType = SharpMetaType.Node;
			Parse(row, defaultPath);
		}

		public SharpMetaNodeRow(SharpMetaNodeRow source)
		{
			Index = source.Index;
			Path = source.Path;
			NodeType = source.NodeType;
			ValueType = source.ValueType;
			AdditionalInfo = source.AdditionalInfo.ToDictionary(x => x.Key, x => new SharpValue(x.Value));
			QueryPath = source.QueryPath.ToDictionary(x => x.Key,
				x => new KeyValuePair<string, string>(x.Value.Key, x.Value.Value));
		}

		public SharpMetaNodeRow(SharpNode source)
		{
			Index = source.Index;
			Path = source.Path;
			NodeType = source.NodeType;
			ValueType = source.ValueType;
			if (!string.IsNullOrEmpty(source.Format))
				AdditionalInfo["Format"] = new SharpValue(source.Format);
			if (source.DefaultValue != null)
				AdditionalInfo["DefaultValue"] = new SharpValue(source.DefaultValue);
		}

		public SharpNodeRow CreateNodeRow(SharpNode node)
		{
			if (node.IsValueNode)
			{
				SharpValue value;

				if (AdditionalInfo.TryGetValue("DefaultValue", out value))
				{
					return new SharpValueRow(node, value.Value) { QueryPath = QueryPath };
				}

				return new SharpValueRow(node, null) { QueryPath = QueryPath };	
			}
			
			return new SharpNodeRow(node) {QueryPath = QueryPath};
		}

		public SharpNode CreateNode()
		{
			var node = new SharpNode
			{
				Name = SharpNode.GetNodeName(Path),
				Index = Index,
				Path = Path,
				IsValueNode = IsValueNode,
				ValueType = ValueType,
				NodeType = NodeType,
			};

			SharpValue value;
			if (AdditionalInfo.TryGetValue("Format", out value))
				node.Format = value.Value.ToString();

			if (!node.IsValueNode && AdditionalInfo.TryGetValue("DefaultValue", out value))
				node.DefaultValue = value;

			return node;
		}

		public bool IsValueNode
		{
			get { return Path.Contains('@') || Path.EndsWith("/#"); }
		}

		public string GetFullQueryPath()
		{
			StringBuilder sb = new StringBuilder();

			foreach (var c in Path)
			{
				if (c == '/')
				{
					KeyValuePair<string, string> item;
					if (QueryPath.TryGetValue(sb.ToString(), out item))
					{
						sb.Append(string.Format("[{0}={1}]", item.Key, item.Value));
					}
				}

				sb.Append(c);
			}
			return sb.ToString();
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();

			if (Index != -1)
				sb.Append(string.Format("${0} = ", Index));
			else
				sb.Append("$ = ");

			sb.Append(GetFullQueryPath());	

			if (NodeType != SharpNodeType.Any || ValueType != SharpValueType.None)
			{
				sb.Append('~');
				if (NodeType != SharpNodeType.Any)
				{
					sb.Append(NodeType);
					if (ValueType != SharpValueType.None)
						sb.Append(' ');
				}

				if (ValueType != SharpValueType.None)
					sb.Append(ValueType);
			}

			SharpValue value;

			if (AdditionalInfo.TryGetValue("Format", out value))
				sb.Append(string.Format("{{{0}}}", value.Value));

			if (AdditionalInfo.TryGetValue("DefaultValue", out value))
			{
				sb.Append(string.Format(" := {0}", value.Value));
			}

			return sb.ToString();
		}

		public void Parse(string row, string defaultPath="")
		{
			AdditionalInfo.Clear();
			QueryPath.Clear();

			ValueType = SharpValueType.None;
			NodeType = SharpNodeType.Any;
						
			StringBuilder sbPath = new StringBuilder();
			StringBuilder sbType = new StringBuilder();
			StringBuilder sbFormat = new StringBuilder();
			StringBuilder sbDefaultValue = new StringBuilder();
			StringBuilder sbQueryPath = new StringBuilder();
			StringBuilder sbQueryValue = new StringBuilder();
			StringBuilder sbIndex = new StringBuilder();

			bool parseIndex = false;
			bool parsePath = true;
			bool parseQueryPath = false;
			bool parseQueryValue = false;
			bool parseType = false;
			bool parseFormat = false;
			bool parseDefaultValue = false;

			bool isValueNode = false;
			bool isAttribute = false;
			bool isPathExpanded = false;
			bool isPathLocked = false;

			int formatCount = 0;

			int pos = 0;
			while (pos < row.Length)
			{
				char c = row[pos++];
				if (char.IsWhiteSpace(c))
					continue;

				if (c == '$')
				{
					parseIndex = true;
				}
				else
				{
					pos --;
				}
				break;
			}

			while(pos < row.Length)
			{
				char c = row[pos++];

				if (parseIndex)
				{
					if (c == '=')
					{
						int index = -1;
						if (int.TryParse(sbIndex.ToString().Trim(), out index))
							Index = index;

						parseIndex = false;
					}
					else
					{
						sbIndex.Append(c);
					}
				}
				else if (parseDefaultValue)
				{
					sbDefaultValue.Append(c);
				}
				else if (parseFormat)
				{
					if (c == '}')
					{
						if (formatCount >= 0)
						{
							parseFormat = false;
							parsePath = true;
							AdditionalInfo["Format"] = new SharpValue(sbFormat.ToString());
						}
						else
						{
							formatCount --;
						}
					}
					else
					{
						if (c == '{') formatCount ++;
						sbFormat.Append(c);
					}
				}
				else if (parseType)
				{
					if (c == ' ' || c == '=' || c == '{' || c == ':')
					{
						var t = sbType.ToString().Trim();

						if (!string.IsNullOrEmpty(t))
						{
							SharpNodeType nt;
							if (Enum.TryParse(t, true, out nt))
								NodeType = nt;

							SharpValueType vt;
							if (Enum.TryParse(t, true, out vt))
								ValueType = vt;
						}

						sbType.Clear();
						if (c == '=' || c == ':')
						{
							parseType = false;
							parsePath = true;
							pos--;
							continue;
						}

						else if (c == '{')
						{
							parseType = false;
							parseFormat = true;
						}
					}
					else
					{
						sbType.Append(c);
					}
				}
				
				else if (parseQueryValue)
				{
					if (c == ']')
					{
						QueryPath[sbPath.ToString()] = new KeyValuePair<string, string>(sbQueryPath.ToString(), sbQueryValue.ToString());
						parseQueryValue = false;
						sbQueryPath.Clear();
						sbQueryValue.Clear();
					}
					else
					{
						sbQueryValue.Append(c);
					}
				}
				else if (parseQueryPath)
				{
					if (c == '=')
					{
						parseQueryPath = false;
						parseQueryValue = true;
					}
					else if (c == ']')
					{
						parseQueryPath = false;
						sbQueryPath.Clear();
						sbQueryValue.Clear();
					}
					else
					{
						sbQueryPath.Append(c);
					}
				}
				else if (parsePath)
				{
					if (char.IsWhiteSpace(c))
						continue;

					if (!isPathExpanded)
					{
						if (c == '~' || c == '=' || c == '{' || c =='.' || c == '@')
						{
							sbPath.Append(defaultPath);
							isPathExpanded = true;
							
							if (c != '.')
							{
								isValueNode = true;
								isPathLocked = true;
								sbPath.Append('/');
							}
							else
							{
								continue;
							}
						}
					}
					else if (c == '.' && !isPathLocked)
					{
						var parts = sbPath.ToString().Split('/').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
						if (parts.Count > 1)
							parts = parts.Take(parts.Count - 1).ToList();
						sbPath = new StringBuilder(String.Join("/", parts));
						continue;
					}

					if (c != '.')
						isPathLocked = true;

					if (c == '[')
					{
						parseQueryPath = true;
					}
					else if (c == '~')
					{
						parseType = true;
						parsePath = false;
					}
					else if (c == '{')
					{
						parseFormat = true;
						parsePath = false;
					}
					else if (c == ':')
					{
						if (pos < row.Length && row[pos] == '=')
						{
							pos++;
							parseDefaultValue = true;
							parsePath = false;
						}
					}
					else if (c == '=')
					{
						isValueNode = true;
						parseDefaultValue = true;
						parsePath = false;
					}
					else
					{
						if (c == '@')
						{
							isAttribute = true;
							isValueNode = true;
						}
						else if (c == '#')
						{
							isValueNode = true;
						}
						else if (c == '/')
						{
							isValueNode = false;
							isAttribute = false;
						}

						sbPath.Append(c);
						isPathExpanded = true;
					}
				}
			}

			if (parseType)
			{
				var t = sbType.ToString().Trim();

				if (!string.IsNullOrEmpty(t))
				{
					SharpNodeType nt;
					if (Enum.TryParse(t, true, out nt))
						NodeType = nt;

					SharpValueType vt;
					if (Enum.TryParse(t, true, out vt))
						ValueType = vt;
				}

				parseType = false;
			}


			Path = sbPath.ToString();

			if (isValueNode && !isAttribute && !Path.EndsWith("/#"))
			{
				if (!Path.EndsWith("/"))
					Path += "/#";
				else
					Path += "#";
			}

			if (parseDefaultValue)
			{
				var defaultValue = sbDefaultValue.ToString().Trim();
				if (defaultValue.StartsWith("\""))
					defaultValue = EncodedString.ParseQuotedString(defaultValue);

				AdditionalInfo["DefaultValue"] = new SharpValue(defaultValue);
			}
		}
	}
	
	internal class SharpMetaNodeMapRow : SharpMetaRow
	{
		public int Index;
		public SharpMapType MapType = SharpMapType.Packed;
		public List<int> Members = new List<int>();
		public List<int> Columns = new List<int>();
		public Dictionary<string, SharpValue> AdditionalInfo = new Dictionary<string, SharpValue>();

		public SharpMetaNodeMapRow()
		{
			
		}

		public SharpMetaNodeMapRow(SharpNodeMap node)
		{
			Index = node.Index;
			MapType = node.MapType;
			var columns = node.Columns;
			Members = columns.Select(x => x.Index).ToList();
			Columns = columns.Select(x => x.Width).ToList();
		}
	}

	internal class SharpMetaConstantRow : SharpMetaRow
	{
		public List<KeyValuePair<int, int>> Index = new List<KeyValuePair<int, int>>();
		public SharpValue Value;
	}

	internal class SharpMetaIndexRow : SharpMetaRow
	{
		public int Index;
		public string Query;
		public List<int> Offsets = new List<int>();
		public List<SharpValue> Values = new List<SharpValue>(); 
		public Dictionary<string, SharpValue> AdditionalInfo = new Dictionary<string, SharpValue>();
	}

	internal class SharpMetaParameterRow : SharpMetaRow
	{
		public string Key;
		public SharpValue Value;

		public SharpMetaParameterRow()
		{
			MetaType = SharpMetaType.Parameter;
		}

		public SharpMetaParameterRow(string key, object value)
		{
			Key = key;
			Value = new SharpValue(value);
		}

		public SharpMetaParameterRow(string row)
		{
			Parse(row);
		}

		public override string ToString()
		{
			return String.Format("#! {0} = {1}", Key, Value);
		}

		public void Parse(string row)
		{
			if (row.StartsWith("#!"))
			{
				var equalStart = row.IndexOf('=');
				if (equalStart >= 0)
				{
					Key = row.Substring(2, equalStart-2).Trim();
					string val = row.Substring(equalStart + 1).Trim();
					Value = new SharpValue(val, true);
				}
			}
		}
	}

	internal class SharpMetaHashRow : SharpMetaRow
	{
		public byte[] HashValue;
	}

	internal class SharpMetaBlockRow : SharpMetaRow
	{
		public Dictionary<string, SharpValue> Header = new Dictionary<string, SharpValue>();
		public byte[] Content;
		public SharpBlockType BlockType = SharpBlockType.None;
		public bool IsCompressed = false;
	}

	#endregion
}
