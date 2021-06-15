using System.Collections.Generic;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpNodePackage
	{
		private Dictionary<string, SharpNode> _index = new Dictionary<string, SharpNode>();

		public string Name = "";

		public void Add(string name, SharpNode node)
		{
			_index[name] = node;
		}

		public SharpNode GetNode(string path)
		{
			if (path == null)
				return null;

			if (_index.ContainsKey(path))
				return _index[path];

			return null;
		}
	}
}
