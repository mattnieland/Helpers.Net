namespace Helpers.Net.IO.SharpFile
{

	#region Sharp File Node Types

	internal enum SharpValueType
	{
		Auto			= -1,
		None			= 0,
		String			= 1,
		Bool			= 2,
		Int				= 3,
		Long			= 4,
		Double			= 5, 
		Decimal			= 6,
		Date			= 7,
		DateTime		= 8,
		TimeStamp		= 9,
		TimeSpan		= 10,
		NumericString	= 11,
		BitField		= 12,
		Enum			= 13,
		List			= 14,
		Object			= 15,
	}

	internal enum SharpNodeType
	{
		Any			= 0,
		Single		= 1,
		Optional	= 2,
		Repeated	= 3,
		Index		= 4,
		Query		= 5,
		Many		= 6,
		Only		= 7,
	}

	#endregion

	#region SharpFile Wire Types

	// WireType is encoded as the first 3 bits of a varint encoded header value
	// The type of header being encoded determines which type the wiretype 
	// refers to.

	internal enum SharpRowType
	{
		None = -1,
		Meta = 0,			// varint encodes meta tag to follow
		Value = 1,			// varint encodes node index with value to follow
		Map = 2,			// varint encodes node index with value mapping to follow
		Node = 3,			// varint encodes node index
		Node8 = 4,			// varint encodes node index with 8 bit size to follow
		Node16 = 5,			// varint encodes node index with 16 bit size to follow
		Node32 = 6			// varint encodes node index with 32 bit size to follow
	}

	internal enum SharpBlockType
	{
		None = -1,
		Node = 0,
		Constant = 1,
		Index = 2,
		Row = 3
	}

	internal enum SharpFieldType
	{
		None = -1,
		Constant = 0,		// varint directly encodes a constant index 
		Number = 1,			// varint directly encodes int or long  
		String = 2,			// varint encodes number of bytes that follow encoded using string encoding (usually UTF8)
		ByteArray = 3,		// varint encodes number of bytes that follow as byte[]
		NumericString = 4,  // varint encodes number of digits that follow (encoded as 2 digits (0-9+-:/. ) per byte)
		NumberArray = 5,	// varint encodes total size of varints to follow as int[] or long[]
		BitField = 6,		// varint directly encodes a sequence of bits as bool[]
		DateTime = 7		// varint directly encodes a DateTime or Timespan using vardate encoding
	}

	internal enum SharpMetaType
	{
		None = -1,
		Comment = 0,		// varint encodes size of comment to follow
		Node = 1,			// varint encodes size of node definition to follow
		NodeMap = 2,		// varint encodes size of node map definition to follow
		Constant = 3,		// varint encodes size of constant definition to follow
		Index = 4,			// varint encodes size of node index to follow
		Parameter = 5,		// varint encodes size of processing parameter to follow
		Hash = 6,			// varint encodes size of hash to follow
		Block = 7			// varint encodes size of compressed block to follow
	}

	internal enum SharpMapType
	{
		None = -1,
		Fixed = 0,				// varint encodes bytes to follow encoded as string (with fixed column sizes)
		Variable = 1,			// varint encodes bytes to follow encoded as string (with variable column sizes)
		Sequence = 2,			// varint encodes bytes to follow encoded as sequence of values
		Packed = 3,				// varint encodes bytes to follow encoded as packed of values
		CompressFixed = 4,		// Same as Fixed with byte stream compressed
		CompressVariable = 5,	// Same as Variable with byte stream compressed
		CompressSequence = 6,	// Same as Sequence with byte stream compressed
		CompressPacked = 7		// Same as Packed with byte stream compressed
	}

	internal enum SharpVarDateType
	{
		None = -1,
		SpanByTick = 0,
		SpanByMilliSecond = 1,
		SpanBySecond = 2,
		SpanByDay = 3,
		TimeByHour = 4,
		TimeByMinute = 5,
		TimeBySecond = 6,
		TimeByMilliSecond = 7,
		DateTime = 8,
		AutoDateTime = 9
	}
	#endregion

	internal enum SharpRowStatus
	{
		Default = 0, 
		Insert = 1,
		Update = 2,
		Delete = 3,
		Cancel = 4
	}
}
