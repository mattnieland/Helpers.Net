using System.Collections;
using System.Collections.Generic;

namespace Helpers.Net.IO.SharpFile
{
	internal class SharpRowCursor : IEnumerator<SharpNodeRow>, IEnumerable<SharpNodeRow>
	{
		private SharpNodeRow _top;
		private SharpNodeRow _current;
		private bool _done = false;

		public SharpRowCursor(SharpNodeRow top)
		{
			_top = top;
			_current = null;
		}

		public SharpRowCursor(SharpRowCursor source)
		{
			_top = source._top;
			_current = source._current;
			_done = source._done;
		}
		
		public bool EndOfRows
		{
			get { return _done; }
		}

		public void MoveToEnd()
		{
			_current = null;
			_done = true;
		}

		public SharpNodeRow Peek()
		{
			return _current;
		}
		
		public SharpNodeRow Current
		{
			get { return _current; }
		}

		public SharpNodeRow Top
		{
			get { return _top; }
		}

		public void Dispose()
		{
		}

		object System.Collections.IEnumerator.Current
		{
			get { return _current; }
		}

		public bool MoveNext()
		{
			if (_done)
			{
				_current = null;
				return false;
			}

			if (_current == null && _top == null)
				return false;

			_current = _current == null ? _top.First : _current.Next;

			if (_current == null)
				_done = true;

			return !_done;
		}

		public void Reset()
		{
			_current = null;
			_done = false;
		}

		public IEnumerator<SharpNodeRow> GetEnumerator()
		{
			return new SharpRowCursor(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
