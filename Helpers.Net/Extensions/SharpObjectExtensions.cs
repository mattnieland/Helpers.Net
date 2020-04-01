using Helpers.Net.Objects;
using System;
using System.Collections.Generic;

namespace Helpers.Net.Extensions
{
	public static class SharpObjectExtensions
	{
		public static SharpObject AsObject(this object obj, bool camelCase = true)
		{
			return SharpObject.Copy(obj).Clone(autoCaseFields: camelCase);			
		}

		public static Dictionary<string, object> AsDictionary(this object obj, bool camelCase = true)
		{
			return SharpObject.Copy(obj).Clone(autoCaseFields: camelCase).ToDictionary();
		}
		public static T ToObject<T>(this SharpObject obj)
		{
			var instance = typeof(T).GetConstructor(new Type[] { }).Invoke(new object[] { });

			foreach (var field in instance.GetType().GetProperties())
			{
				var fieldName = field.Name.CamelCase();
				field.SetValue(instance, obj[fieldName]);
			}
			return (T)instance;
		}
	}
}
