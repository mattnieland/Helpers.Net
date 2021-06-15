using System.Collections.Generic;
using System.Linq;

namespace Helpers.Net.IO.SharpFile
{
    public class SharpObjectPath
	{
		public string Path;
		public SharpObject Value;

		public SharpObjectPath(string path, SharpObject value)
		{
			Path = path;
			Value = value;
		}

		public SharpObjectPath(string path, object value)
		{
			Path = path;
			Value = SharpObject.Copy(value);
		}
	}

	public class SharpObjectPathSet
	{
		public string Path;
		public IEnumerable<SharpObject> Values;

		public SharpObjectPathSet(string path, IEnumerable<SharpObject> values)
		{
			Path = path;
			Values = values;
		}

		public SharpObjectPathSet(string path, IEnumerable<object> values)
		{
			Path = path;
			Values = values.Select(x => SharpObject.Copy(x));
		}
	}

	
}
