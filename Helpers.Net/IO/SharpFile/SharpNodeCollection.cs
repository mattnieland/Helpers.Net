using System.Collections.Generic;
using System.Linq;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpNodeCollection 
	{
		private readonly Dictionary<int, SharpNode> _nodes = new Dictionary<int, SharpNode>();
		private readonly Dictionary<string, SharpNodePackage> _packages = new Dictionary<string, SharpNodePackage>();

		private SharpNodePackage _currentPackage = null;
		private string _rootPackage = null;

		private int _nextIndex = 0;

		public SharpNodeCollection(string root = null)
		{
			_rootPackage = root ?? "";
			Clear();
		}

		public void Clear()
		{
			_nextIndex = 0;

			_nodes.Clear();
			_packages.Clear();
			_currentPackage = GetPackage(_rootPackage);
		}

		public SharpNodePackage CurrentPackage {get { return _currentPackage;}}

		public int NextIndex
		{
			get { return _nextIndex; }
			set { _nextIndex = value; }
		}

		public IEnumerable<SharpRow> GetRows()
		{
			string lastPackage = "";

			foreach (var node in _nodes.Values.OrderBy(n=>n.Index))
			{
				if (node.PackageName != lastPackage)
				{
					yield return new SharpMetaParameterRow("Package", node.PackageName);
					lastPackage = node.PackageName;
				}
				if (node is SharpNodeMap)
					yield return new SharpMetaNodeMapRow(node as SharpNodeMap);
				else
					yield return new SharpMetaNodeRow(node);
			}
		}

		public IEnumerable<SharpNode> GetNodes()
		{
			return _nodes.Values.OrderBy(x=>x.Index);
		}

		public IEnumerable<int> GetNodeIds()
		{
			return _nodes.Keys.OrderBy(x => x);
		}

		public void Add(SharpNode node)
		{
			if (node.Parent == null)
				AddParents(node);

			node.IntializeNode(this, _currentPackage, _nextIndex++);

			_currentPackage.Add(node.Path, node);
			_nodes.Add(node.Index, node);
			node.UpdateNodeLinks();
		}

		public void Insert(SharpNode node)
		{
			if (node.Parent == null)
				AddParents(node);
			
			node.IntializeNode(this, _currentPackage);

			if (node.Index > _nextIndex)
				_nextIndex = node.Index + 1;

			_currentPackage.Add(node.Path, node);
			_nodes.Add(node.Index, node);

			node.UpdateNodeLinks();
		}

		private void AddParents(SharpNode node)
		{
			string path = node.GetParentPath();

			if (!string.IsNullOrEmpty(path))
			{
				var parent = _currentPackage.GetNode(path);
				if (parent == null)
				{
					Add(new SharpNode() {Path = path, Name = SharpNode.GetNodeName(path)});
				}
			}
		}

		public void SetCurrentPackage(string package)
		{
			_currentPackage = GetPackage(package);
		}

		public SharpNode GetOrCreateNode(List<string> nodePath, bool isLeafNode, SharpNode parent=null)
		{
			var path = SharpNode.GetNodePath(nodePath, isLeafNode);
			var node = GetNode(path);
			if (node != null)
				return node;

			node = new SharpNode
			{
				Path = path, 
				Name = nodePath.Last(),
				Parent = parent
			};

			node.IsValueNode = node.Name.StartsWith("@") || isLeafNode;

			Add(node);
			return node;
		}

		public SharpNode GetNode(string nodePath)
		{
			return _currentPackage.GetNode(nodePath);
		}

		public SharpNode GetNode(int index)
		{
			if (_nodes.ContainsKey(index))
				return _nodes[index];

			return null;
		}

		public IEnumerable<SharpNode> GetNodes(IEnumerable<int> indexes)
		{
			foreach (var id in indexes)
			{
				if (_nodes.ContainsKey(id))
					yield return _nodes[id];
				else
					yield return null;
			}
		}

		public SharpNodePackage GetPackage(string package)
		{
			if (!_packages.ContainsKey(package))
			{
				_packages[package] = new SharpNodePackage() { Name = package };
			}

			return _packages[package];
		}
	}

}
