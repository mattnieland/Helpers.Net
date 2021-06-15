using Helpers.Net.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Helpers.Net.IO.SharpFile
{
	public class SharpObjectMap
	{
		public SharpObject Map = new SharpObject();
		public SharpObject Default = new SharpObject();

		public bool MergeSourceFields = false;
		public bool CopyRecordFields = false;
		public bool RequireMappingFields = false;

		public string RootNode = "";
		public string RecordFieldNode = "";

		public SharpObject Apply(SharpObject record, SharpObject context)
		{
			var obj = ApplyMap(Map, record, context);

			if (MergeSourceFields)
			{
				obj.MergeWith(record);
			}

			if (CopyRecordFields)
			{
				var target = RootNode == RecordFieldNode ? obj : obj.GetMemberObject(RecordFieldNode, o => o);
				if (target == null)
				{
					target = new SharpObject();
					obj[RecordFieldNode] = target;
				}

				foreach (var item in record)
				{
					if (!target.ContainsKey(item.Key))
					{
						target[item.Key] = item.Value;
					}
				}
			}

			return obj;
		}

		private SharpObject ApplyMap(SharpObject map, SharpObject record, SharpObject context)
		{
			var obj = new SharpObject();

			foreach (var item in map)
			{
				if (item.Value is SharpObject)
				{
					obj[item.Key] = ApplyMap((SharpObject) item.Value, record, context);
				}
				else if (item.Value is SharpObjectList)
				{
					foreach (var child in (SharpObjectList) item.Value)
					{
						obj.Add(item.Key, ApplyMap(child, record, context));
					}
				}
				else
				{
					if (item.Value is string)
					{
						object fieldValue = record.Format((string)item.Value);
						
						string stringValue = fieldValue.ToString();
						if (SharpObject.IsFormatRequired(stringValue))
						{
							fieldValue = obj.Format(stringValue);
							stringValue = fieldValue.ToString();
						}

						if (SharpObject.IsFormatRequired(stringValue))
						{
							fieldValue = null;
							stringValue = "";
						}

						var fieldName = item.Key;
						var fieldParts = fieldName.Split('_');
						if (fieldParts.Length == 2)
						{
							var fieldType = fieldParts[1].ToLower();
							switch (fieldType)
							{
								case "bool":
									fieldName = fieldParts[0];
									if (string.IsNullOrEmpty(stringValue))
										fieldValue = false;
									else if (stringValue == "0" || stringValue == "1")
										fieldValue = stringValue == "1";
									else if (stringValue.StartsWith("y", StringComparison.OrdinalIgnoreCase) || stringValue.StartsWith("n", StringComparison.OrdinalIgnoreCase))
										fieldValue = stringValue.StartsWith("y", StringComparison.OrdinalIgnoreCase);
									else
										fieldValue = Convert.ToBoolean(stringValue);

									break;
								case "int":
									fieldName = fieldParts[0];
									fieldValue = Convert.ToInt32(stringValue);
									break;
								case "long":
									fieldName = fieldParts[0];
									fieldValue = Convert.ToInt64(stringValue);
									break;
								case "datetime":
									fieldName = fieldParts[0];
									fieldValue = Convert.ToDateTime(stringValue);
									break;
								case "decimal":
									fieldName = fieldParts[0];
									fieldValue = Convert.ToDecimal(stringValue);
									break;
								case "list":
									fieldName = fieldParts[0];
									fieldValue = EncodedString.ParseList(stringValue);
									break;
							}
						}

						obj[fieldName] = fieldValue;
					}
					else
						obj[item.Key] = item.Value;
				}
			}


			return obj;

		}
		public void LoadMap(SharpObject map)
		{
			map.CopyTo(this);

			Map = map.GetMemberObject(RootNode, o=>o);
		}
	}
}
