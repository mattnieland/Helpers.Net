using Helpers.Net.Objects;
using System;

namespace Helpers.Net.Extensions
{
	public static class SharpObjectExtensions
	{
		public static SharpObject AsObject(this object obj)
		{
			return SharpObject.Copy(obj);
		}
		public static object AsObject<T>(this SharpObject obj)
		{
			var instance = typeof(T).GetConstructor(new Type[] { }).Invoke(new object[] { });

			foreach (var field in instance.GetType().GetProperties())
			{
				var fieldName = Char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1);
				field.SetValue(instance, obj[fieldName]);
			}
			return instance;
		}
	}
}
