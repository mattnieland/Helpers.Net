using System.Collections.Generic;

namespace Helpers.Net.IO.SharpFile
{
    internal class SharpRowStream : SharpRowCollection
	{
		private IEnumerable<SharpRow> _streamSource = null;

		public IEnumerable<SharpRow> StreamSource
		{
			get { return _streamSource; }
			set { _streamSource = value; }
		}

		private SharpNode _streamNode = null;

		public SharpNode StreamNode
		{
			get { return _streamNode; }
			set { _streamNode = value; }
		}

		public override IEnumerable<SharpRow> GetRows(SharpNode node = null)
		{
			if (node == null && _streamSource != null)
			{
				AddRange(_streamSource);

				foreach (var row in DequeueInsertRows())
					yield return row;

				_streamSource = null;
			}

			if (_streamSource == null || node == null)
				yield break;

			List<SharpRow> rows = new List<SharpRow>();
			var streamEnumerator = _streamSource.GetEnumerator();

			while (streamEnumerator.MoveNext())
			{
				var nodeRow = streamEnumerator.Current as SharpNodeRow;
				if (nodeRow == null)
					continue;

				if (nodeRow.Node == node || !node.HasDescendant(nodeRow.Node))
				{
					if (rows.Count > 0)
					{
						AddRange(rows, insertParentNodes: true, skipFirstParentRow: true);
						rows.Clear();
						foreach (var row in DequeueInsertRows())
							yield return row;
					}

					if (nodeRow.Node == node)
						rows.Add(nodeRow);
					continue;
				}

				rows.Add(nodeRow);
			}

			if (rows.Count > 0)
			{
				AddRange(rows, insertParentNodes: true, skipFirstParentRow: true);
				rows.Clear();
				foreach (var row in DequeueInsertRows())
					yield return row;
			}
		}
	}
}
