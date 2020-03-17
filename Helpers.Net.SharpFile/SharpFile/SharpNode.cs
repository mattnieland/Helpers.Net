using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Helpers.Net.Objects
{
	public class SharpNodeSchema
	{
		public string Path;
		public string Name;
		public string ValueType;
		public string NodeType;
		public bool IsValueNode;

		public Dictionary<string, SharpNodeSchema> Members = new Dictionary<string, SharpNodeSchema>();

	}

	internal class SharpNode
	{
		protected SharpNodePackage Package;
		protected SharpNodeCollection Nodes;
		protected readonly Dictionary<int, int> Constants = new Dictionary<int, int>();
 
		public string Path = "";
		public string Name = "";
		public string ClassName = null;

		public SharpValueType ValueType = SharpValueType.None;
		public SharpNodeType NodeType = SharpNodeType.Any;
		public string Format = "";

		public SharpValue DefaultValue = null;

		public int Index = -1;
		public bool IsValueNode = false;

		private bool? _leafFlag = null;
		private bool? _expandedValueFlag = null;
		private bool? _attributeFlag = null;

		public SharpNode Parent = null;
		public HashSet<int> Descendants = new HashSet<int>();
		public Dictionary<string, SharpNode> Members = new Dictionary<string, SharpNode>();
		
		public SharpNode()
		{
		}

		public void IntializeNode(SharpNodeCollection nodes, SharpNodePackage package, int? index=null)
		{
			Nodes = nodes;
			Package = package;
			if(index != null)
				Index = (int)index;
		}

		public SharpNodeSchema NodeSchema
		{
			get
			{
				var result = new SharpNodeSchema
				{
					Name = Name,
					IsValueNode = IsValueNode,
					NodeType = NodeType.ToString(),
					Path = Path,
					ValueType = ValueType.ToString()
				};

				foreach (var child in Members)
				{
					result.Members[child.Key] = child.Value.NodeSchema;
				}

				return result;
			}
		}

		public string PackageName { get { return Package != null ? Package.Name : ""; } }

		public virtual IEnumerable<SharpRow> GetRows(IEnumerable<object> values)
		{
			if (IsValueNode)
			{
				if (values != null)
					yield return new SharpValueRow(this, values);
				else
					yield return new SharpValueRow(this, null);
			}
			else
				yield return new SharpNodeRow(this);
		}

		public override string ToString()
		{
			return Path;
		}

		public SharpNode GetParentNode()
		{
			if (IsExpandedValueNode)
				return Parent.Parent;

			return Parent;
		}

		public bool IsListNodeType()
		{
			if (NodeType == SharpNodeType.Repeated || NodeType == SharpNodeType.Many)
				return true;
			return false;
		}

		public string GetRootPath()
		{
			if (IsLeafNode)
			{
				// Trim last '/#'
				return Path.Substring(0, Path.Length - 2);
			}
			else
			{
				return Path;
			}
		}

		public bool IsAttributeNode
		{
			get
			{
				if (!IsValueNode) return false;

				if (_attributeFlag == null)
					_attributeFlag = Name.Length > 0 && Name[0] == '@';

				return (bool)_attributeFlag;
			}

			set
			{
				_attributeFlag = value;
				if (value)
					IsValueNode = true;
			}
		}

		public virtual bool IsSingleValueNode
		{
			get
			{
				return IsValueNode;
			}
		}

		public bool IsLeafNode
		{
			get
			{
				if (!IsValueNode) return false;

				if (_leafFlag == null)
					_leafFlag = Path.EndsWith("/#");

				return (bool)_leafFlag;
			}

			set
			{
				_leafFlag = value;
				if (value)
					IsValueNode = true;
			}
		}

		public bool IsExpandedValueNode
		{
			get
			{
				if (!IsValueNode)
					return false;

				if (_expandedValueFlag == null)
				{
					var nodePath = Path;
					if (nodePath.EndsWith("/#"))
						nodePath = nodePath.Substring(0, nodePath.Length - 2);

					_expandedValueFlag = Parent != null && nodePath == Parent.Path;
				}

				return (bool)_expandedValueFlag;
			}
			set
			{
				_expandedValueFlag = value;
				if (value)
					IsValueNode = true;
			}
		}
		
		public bool HasDescendant(int index)
		{
			return Descendants.Contains(index);
		}

		public bool HasDescendant(SharpNode node)
		{
			return Descendants.Contains(node.Index);
		}

		public List<SharpNode> PathTo(SharpNode ancestor)
		{
			var result = new List<SharpNode>();

			var node = Parent;
			while (node != null && node != ancestor)
			{
				result.Insert(0, node);
				node = node.Parent;
			}

			return result;
		}

		public List<SharpNode> PathFrom(SharpNode ancestor)
		{
			if (Parent == null)
				return new List<SharpNode>();

			SharpNodeStack stack = new SharpNodeStack();
			SharpNode start = Parent;

			if (IsValueNode)
			{
				var nodePath = Path;
				if (nodePath.EndsWith("/#"))
					nodePath = nodePath.Substring(0, nodePath.Length - 2);
				if (nodePath == Parent.Path)
					start = Parent.Parent;
			}

			if (start == null || ancestor == start || !ancestor.HasDescendant(start))
				return new List<SharpNode>();

			stack.Enqueue(start);
			while (stack.Top != ancestor)
			{
				if (stack.Top.Parent == null || stack.Top == stack.Top.Parent)
					break;

				stack.Enqueue(stack.Top.Parent);
			}

			return stack.ToList();
		}

		public virtual IEnumerable<SharpNode> GetNodes()
		{
			yield return this;
		}

		public void UpdateNodeLinks()
		{
			SharpNode parent = null;

			if (IsValueNode && !IsSingleValueNode)
			{
				parent = GetNodes().Select(x => x.Parent).FirstOrDefault();
			}
			else
			{
				if (IsLeafNode)
					parent = Package.GetNode(GetRootPath());

				if (parent == null)
					parent = Package.GetNode(GetParentPath(Path));
			}

			Parent = parent;
			UpdateAncestors();
		}

		private void UpdateAncestors()
		{
			var parent = Parent;
			while (parent != null)
			{
				parent.Descendants.Add(Index);
				if (parent.Parent == parent)
					break;

				parent = parent.Parent;
			}

			if (!IsValueNode || IsSingleValueNode)
			{
				if (Parent != null && !Parent.Members.ContainsKey(Name))
					Parent.Members[IsExpandedValueNode ? "#" : Name] = this;
			}

		}

		public string GetParentPath()
		{
			return GetParentPath(Path);
		}

		#region Static Wrapper Code Generation
		
		private string NormalizeMemberName(string name)
		{
			StringBuilder sb = new StringBuilder();
			List<char> skipChar = new List<char> {' ', '_', '.', '@'};
			bool capitalizeNext = true;

			bool isAllUpper = name.All(c => char.IsUpper(c) || c=='_');
			
			foreach (var c in name)
			{
				if (skipChar.Contains(c))
				{
					capitalizeNext = true;
					continue;
				}

				if (capitalizeNext)
					sb.Append(char.ToUpperInvariant(c));
				else
				{
					if (isAllUpper)
						sb.Append(char.ToLowerInvariant(c));
					else
						sb.Append(c);
				}

				capitalizeNext = false;
			}

			return sb.ToString();
		}

//		private CodeTypeDeclaration GenerateClass(Dictionary<string, CodeTypeDeclaration> ns,  string classNamePrefix = "", string baseClassName = null, bool isBaseISharpObject=true)
//		{
//			string className = string.Format("{0}{1}", classNamePrefix, ClassName ?? NormalizeMemberName(Name));
			
//			var nodeClass = new CodeTypeDeclaration(className);
//			ns[className] = nodeClass;

//			if (!string.IsNullOrEmpty(baseClassName))
//				nodeClass.BaseTypes.Add(new CodeTypeReference(baseClassName));

//			nodeClass.IsClass = true;
//			nodeClass.IsPartial = true;

//			nodeClass.BaseTypes.Add("ISharpObject");
//			nodeClass.BaseTypes.Add("ISharpObjectFormat");

//			// Constructor taking SharpObject parameter
//			var constructor = new CodeConstructor();
			
//			if(!string.IsNullOrEmpty(baseClassName) && isBaseISharpObject)
//				constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("obj"));

//			constructor.Attributes = MemberAttributes.Public;
//			constructor.Parameters.Add(new CodeParameterDeclarationExpression("SharpObject", "obj"));
//			constructor.Statements.Add(
//				new CodeAssignStatement( 
//					new CodeVariableReferenceExpression("_obj"), 
//					new CodeArgumentReferenceExpression("obj"))
//				);

//			nodeClass.Members.Add(constructor);

//			constructor = new CodeConstructor();
//			constructor.Attributes = MemberAttributes.Public;

//			var ifStatement = new CodeConditionStatement(
//				new CodeBinaryOperatorExpression(
//					new CodeVariableReferenceExpression("_obj"), 
//					CodeBinaryOperatorType.IdentityEquality,
//					new CodePrimitiveExpression(null)
//					)
//				);

//			ifStatement.TrueStatements.Add( 
//				new CodeAssignStatement(
//					new CodeVariableReferenceExpression("_obj"),
//					new CodeObjectCreateExpression("SharpObject")) );

//			constructor.Statements.Add(ifStatement);

//			nodeClass.Members.Add(constructor);

//			if (string.IsNullOrEmpty(baseClassName))
//			{
//				nodeClass.Members.Add(new CodeSnippetTypeMember(@"
//		public object this[string key]
//		{
//			get { return _obj[key]; }
//			set { _obj[key] = value; }
//		}

//		public bool ContainsField(string key) { return _obj.ContainsField(key); }
//		public object GetFieldValue(string key) { return _obj.GetFieldValue(key); }
//"
				
//					));

//			}
//			// Protected SharpObject 
//			if (string.IsNullOrEmpty(baseClassName) || !isBaseISharpObject)
//			{
//				nodeClass.Members.Add(new CodeMemberField("SharpObject", "_obj") {Attributes = MemberAttributes.Family});

//				// Static Create Method
//				var asStatic = new CodeMemberMethod();
//				asStatic.Name = "Create";
//				asStatic.Parameters.Add(new CodeParameterDeclarationExpression("SharpObject", "obj"));
//				asStatic.Attributes = MemberAttributes.Public | MemberAttributes.Static;
//				asStatic.ReturnType = new CodeTypeReference(className);

//				asStatic.Statements.Add(new CodeMethodReturnStatement(
//					new CodeObjectCreateExpression(className, new CodeArgumentReferenceExpression("obj"))
//					));

//				nodeClass.Members.Add(asStatic);

//				var getObject = new CodeMemberMethod();
//				getObject.Name = "GetObject";
//				getObject.Attributes = MemberAttributes.Public | MemberAttributes.Final;
//				getObject.ReturnType = new CodeTypeReference("SharpObject");
//				getObject.Statements.Add(new CodeMethodReturnStatement(
//					new CodeVariableReferenceExpression("_obj")));

//				nodeClass.Members.Add(getObject);
//			}

//			// Add Public Properties for all child nodes
//			foreach (var member in Members)
//			{
//				var memberNode = member.Value;
//				var memberName = memberNode.ClassName ?? NormalizeMemberName(memberNode.Name);
//				if (memberNode.ClassName == null && memberNode.IsExpandedValueNode)
//					memberName = "Value";

//				var property = new CodeMemberProperty
//				{
//					Name = memberName,
//					Attributes = MemberAttributes.Public | MemberAttributes.Final
//				};

//				if (memberNode.IsValueNode)
//				{
//					bool asList = false;
//					var fieldType = SharpValue.GetFieldType(memberNode.ValueType).ToString();
//					if (memberNode.NodeType == SharpNodeType.Repeated)
//					{
//						property.Type = new CodeTypeReference(string.Format("IList<{0}>", fieldType) );
//						asList = true;
//					}
//					else
//					{
//						property.Type = new CodeTypeReference(fieldType);
//					}

//					CodeExpression defaultValueExpression = SharpValue.IsPrimitiveType(memberNode.ValueType)
//						? (CodeExpression) (new CodePrimitiveExpression(SharpValue.GetDefaultValue(memberNode.ValueType)))
//						: new CodeObjectCreateExpression(fieldType);

//					if (asList)
//					{
//						var listType = new CodeTypeReference(typeof(List<>));
//						listType.TypeArguments.Add(SharpValue.GetFieldType(memberNode.ValueType).ToString());

//						defaultValueExpression = new CodeObjectCreateExpression(listType);
//					}

//					if (memberNode.NodeType != SharpNodeType.Optional)
//					{
//						constructor.Statements.Add(
//							new CodeAssignStatement(
//								new CodeArrayIndexerExpression(
//									new CodeVariableReferenceExpression("_obj"),
//									new CodePrimitiveExpression(member.Key)),
//								defaultValueExpression));
//					}

//					property.GetStatements.Add(
//						new CodeMethodReturnStatement(
//								new CodeMethodInvokeExpression(
//									new CodeMethodReferenceExpression(
//										new CodeVariableReferenceExpression("_obj"),
//										asList ? "GetMemberValueList" : "GetMemberValue",
//										new CodeTypeReference(fieldType) ),
//									new CodePrimitiveExpression(member.Key)
//								)));
//				}
//				else
//				{
//					var memberClassName = string.Format("{0}{1}", className, memberName);

//					if (!ns.ContainsKey(memberClassName))
//						ns[memberClassName] = memberNode.GenerateClass(ns, className);

//					bool asList = false;
//					bool asEnumerable = false;
//					if (memberNode.NodeType == SharpNodeType.Repeated)
//					{
//						property.Type = new CodeTypeReference(string.Format("IList<{0}>", memberClassName));
//						asList = true;
//					}
//					else if (memberNode.NodeType == SharpNodeType.Many)
//					{
//						asEnumerable = true;
//						property.Type = new CodeTypeReference(string.Format("IEnumerable<{0}>", memberClassName));
//					}
//					else
//					{
//						property.Type = new CodeTypeReference(memberClassName);
//					}

//					CodeExpression defaultValueExpression;

//					if (asList || asEnumerable)
//					{
//						var listType = new CodeTypeReference(typeof(List<>));
//						listType.TypeArguments.Add(memberClassName);

//						defaultValueExpression = new CodeObjectCreateExpression(listType);
//					}
//					else
//					{
//						defaultValueExpression = new CodeObjectCreateExpression(memberClassName);
//					}

					
//					constructor.Statements.Add(
//						new CodeAssignStatement(
//							new CodeArrayIndexerExpression(
//								new CodeVariableReferenceExpression("_obj"),
//								new CodePrimitiveExpression(member.Key)),
//								defaultValueExpression));

//					property.GetStatements.Add(
//						new CodeMethodReturnStatement(
//							new CodeMethodInvokeExpression(
//								new CodeMethodReferenceExpression(
//									new CodeVariableReferenceExpression("_obj"),
//									asList ? "GetMemberObjectList" : asEnumerable ? "GetMemberObjectEnumerable" : "GetMemberObject",
//									new CodeTypeReference(memberClassName) ),
//								new CodePrimitiveExpression(member.Key),
//								new CodeMethodReferenceExpression(new CodeTypeReferenceExpression(memberClassName), "Create" ) 
//							)));

//				}

//				property.SetStatements.Add(
//					new CodeAssignStatement(
//						new CodeArrayIndexerExpression(
//							new CodeVariableReferenceExpression("_obj"),
//							new CodePrimitiveExpression(member.Key)),
//						new CodePropertySetValueReferenceExpression() ));
				
//				property.HasSet = true;
//				property.HasGet = true;
//				nodeClass.Members.Add(property);
//			}

//			return nodeClass;
//		}

		//public CodeCompileUnit GenerateStaticWrapper(string nameSpace, string classNamePrefix="", string baseClassname=null, string baseClassNamespace=null)
		//{
		//	var compileUnit = new CodeCompileUnit();

		//	CodeNamespace ns = new CodeNamespace(nameSpace);
		//	ns.Imports.Add(new CodeNamespaceImport("System"));
		//	ns.Imports.Add(new CodeNamespaceImport("System.Collections.Generic"));
		//	ns.Imports.Add(new CodeNamespaceImport("System.Linq"));
		//	ns.Imports.Add(new CodeNamespaceImport("Helpers.Net.Objects"));

		//	if (!string.IsNullOrEmpty(baseClassNamespace))
		//	{
		//		ns.Imports.Add( new CodeNamespaceImport(baseClassNamespace));	
		//	}

		//	Dictionary<string, CodeTypeDeclaration> classes = new Dictionary<string, CodeTypeDeclaration>();

		//	GenerateClass(classes, classNamePrefix, baseClassname);

		//	ns.Types.AddRange(classes.Values.ToArray());

		//	compileUnit.Namespaces.Add(ns);

		//	return compileUnit;
		//}
		#endregion 

		#region Static Methods

		public static string GetParentPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			if (path.Length > 1 && path[path.Length - 1] == '#')
			{
				path = path.Substring(0, path.Length - 2);
			}

			int lastSep = path.LastIndexOf('/');
			if (lastSep == -1)
				return null;

			return path.Substring(0, lastSep);
		}

		public static List<string> SplitNodePath(string path)
		{
			List<string> parts = new List<string>();
			StringBuilder sb = new StringBuilder();

			if (path == null)
				return parts;

			foreach (var c in path)
			{
				switch (c)
				{
					case '/':
						parts.Add(sb.ToString());
						sb.Clear();
						break;
					default:
						sb.Append(c);
						break;
				}
			}

			if (sb.Length > 0)
				parts.Add(sb.ToString());

			return parts;
		}

		public static string GetNodeName(string path)
		{
			
			if (path.EndsWith("/#"))
				path = path.Substring(0, path.Length - 2);

			int lastSep = path.LastIndexOf("/");
			return path.Substring(lastSep+1);
		}

		public static string GetNodePath(List<string> path, bool isLeafNode)
		{
			StringBuilder sb = new StringBuilder();

			for (int i = 0; i < path.Count; i++)
			{
				var item = path[i];

				if (i > 0)
				{
					sb.Append('/');
				}

				sb.Append(item);
			}

			if (isLeafNode)
				sb.Append("/#");

			return sb.ToString();
		}

		#endregion
	}

	internal class SharpNodeStack : IEnumerable<SharpNode>
	{
		private List<SharpNode> _stack = new List<SharpNode>();

		public void Clear()
		{
			_stack.Clear();
		}

		public SharpNode Top { get { return _stack.Count > 0 ? _stack[0] : null; } }
		public bool IsEmpty { get { return _stack.Count == 0; } }
		public int Count { get { return _stack.Count; } }
		public bool Contains(SharpNode node)
		{
			return _stack.Contains(node);
		}

		public void Enqueue(SharpNode node)
		{
			_stack.Insert(0, node);
		}

		public SharpNode Dequeue()
		{
			if (_stack.Count > 0)
			{
				var result = _stack[0];
				_stack.RemoveAt(0);
				return result;
			}

			return null;
		}

		public IEnumerator<SharpNode> GetEnumerator()
		{
			return _stack.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return _stack.GetEnumerator();
		}
	}
	
}
