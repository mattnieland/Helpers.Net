using System;
using System.Collections.Generic;

namespace Helpers.Net.Utilities
{
	/// This class is based on the USPS website definitions on the below page.
	/// http://www.usps.com/ncsc/lookups/abbreviations.html
	/// 
	public class Usps
	{
		#region Abbreviations
		
		private static readonly HashSet<string> UnitedStatesAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "US", "USA", "UNITED STATES" };
		
		private static readonly HashSet<string> StateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware",
												 "District of Columbia", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa",
												 "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota",
												 "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
												 "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon",
												 "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", 
												 "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming" };

		private static readonly HashSet<string> StateAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", 
														 "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", 
														 "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", 
														 "UT", "VT", "VA", "WA", "WV", "WI", "WY" };

		private static readonly HashSet<string> MilitaryStateFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Armed Forces Africa", "Armed Forces Americas", "Armed Forces Canada", "Armed Forces Europe", "Armed Forces Middle East", "Armed Forces Pacific" };

		private static readonly HashSet<string> MilitaryStateAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AA", "AE", "AP" };

		private static readonly HashSet<string> PossessionFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "American Samoa", "Federated States of Micronesia", "Guam", "Marshall Islands", "Northern Mariana Islands", "Palau", "Puerto Rico", "Virgin Islands" };

		private static readonly HashSet<string> PossessionAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AS", "FM", "GU", "MH", "MP", "PW", "PR", "VI" };

		private static readonly HashSet<string> CanadianProvinceAbbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "QC", "ON", "MB", "AB", "BC", "NB", "NL", "NT", "NS", "NU", "PE", "SK", "YT" };

		private static readonly HashSet<string> NonContinentalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HI", "HAWAII", "AK", "ALASKA"};
		
		#endregion

		public static bool IsValidUnitedStatesCountry(string country)
		{
			return !string.IsNullOrEmpty(country) && (UnitedStatesAbbreviations.Contains(country) || IsValidPossession(country));
		}

		public static bool IsValidContinentalState(string state)
		{
			if (string.IsNullOrEmpty(state) || (!StateNames.Contains(state) && !StateAbbreviations.Contains(state)))
				return false;

			return !NonContinentalStates.Contains(state);
		}

		public static bool IsValidState(string state)
		{
			return !string.IsNullOrEmpty(state) && (StateNames.Contains(state) || StateAbbreviations.Contains(state));
		}
	
		public static bool IsValidMilitaryState(string state)
		{
			return !string.IsNullOrEmpty(state) && (MilitaryStateFullNames.Contains(state) || MilitaryStateAbbreviations.Contains(state));
		}

		public static bool IsValidPossession(string possession)
		{
			return !string.IsNullOrEmpty(possession) && (PossessionFullNames.Contains(possession) || PossessionAbbreviations.Contains(possession));
		}

		public static bool IsValidCanadianProvince(string province)
		{
			return !string.IsNullOrEmpty(province) && CanadianProvinceAbbreviations.Contains(province);
		}

		public static TimeSpan GetWorkingDays(DateTime startDate, DateTime endDate)
		{
			var allRelatedHolidays = GetAnnualSchedule(startDate.Year);

			// if the dates span years get all the holiday dates for all years.
			if (!endDate.Year.Equals(startDate.Year))
			{
				var numberOfYearsOff = endDate.Year - startDate.Year;
				for (int i = 1; i <= numberOfYearsOff; i++)
				{
					var additionalYearsHolidays = GetAnnualSchedule(startDate.Year + i);
					foreach (KeyValuePair<string, DateTime> aHoliday in additionalYearsHolidays)
					{
						var itemKey = aHoliday.Key + endDate.Year + "_" + aHoliday.Value;
						allRelatedHolidays.Add(itemKey, aHoliday.Value);

					}
				}
			}

			var dsInterval = endDate - startDate;

			var businessDays = dsInterval.Days + 1;
			var fullWeekCount = businessDays / 7;
			// find out if there are weekends during the time exceedng the full weeks 
			if (businessDays > fullWeekCount * 7)
			{
				// we are here to find out if there is a 1-day or 2-days weekend 
				// in the time interval remaining after subtracting the complete weeks 
				var firstDayOfWeek = (int)startDate.DayOfWeek;
				var lastDayOfWeek = (int)endDate.DayOfWeek;
				if (lastDayOfWeek < firstDayOfWeek)
				{
					lastDayOfWeek += 7;
				}

				if (firstDayOfWeek <= 6)
				{
					if (lastDayOfWeek >= 7) //Both Saturday and Sunday are in the remaining time interval 
					{
						businessDays -= 2;
					}
					else if (lastDayOfWeek >= 6) //Only Saturday is in the remaining time interval 
					{
						businessDays -= 1;
					}
				}
				else if (firstDayOfWeek <= 7 && lastDayOfWeek >= 7)// Only Sunday is in the remaining time interval 
				{
					businessDays -= 1;
				}
			}

			// subtract the weekends during the full weeks in the interval 
			businessDays -= fullWeekCount + fullWeekCount;

			// subtract the number of bank holidays during the time interval 
			foreach (var bankHoliday in allRelatedHolidays.Values)
			{
				if (startDate <= bankHoliday.Date && bankHoliday.Date <= endDate)
				{
					--businessDays;
				}
			}

			var workingDays = new TimeSpan(businessDays, 0, 0, 0);

			return workingDays;
		}

		public static Dictionary<string, DateTime> GetAnnualSchedule(int year)
		{
			var schedule = new Dictionary<string, DateTime>();
			schedule.Add("New Years Day", GetNewYearsDay(year));
			schedule.Add("Memorial Day", GetMemorialDay(year));
			schedule.Add("Independence Day", GetIndependenceDay(year));
			schedule.Add("Labor Day", GetLaborDay(year));
			schedule.Add("Thanksgiving Day", GetThanksgivingDay(year));
			schedule.Add("Thanksgiving - Day After", GetThanksgivingDay(year).AddDays(1));
			schedule.Add("Christmas Day", GetChristmasDay(year));
			schedule.Add("Christmas - Day After", GetChristmasDay(year).Subtract(TimeSpan.FromDays(1)));
			return schedule;
		}

		public static DateTime GetNewYearsDay(int year)
		{
			return new DateTime(year, 1, 1);
		}

		public static DateTime GetMemorialDay(int year)
		{
			DateTime memDay = new DateTime(year, 5, 31);
			while (memDay.DayOfWeek != DayOfWeek.Monday)
			{
				memDay = memDay.AddDays(-1);
			}

			return memDay;
		}

		public static DateTime GetIndependenceDay(int year)
		{
			return new DateTime(year, 7, 4);
		}

		public static DateTime GetLaborDay(int year)
		{
			DateTime laborDay = new DateTime(year, 9, 1);
			while (laborDay.DayOfWeek != DayOfWeek.Monday)
			{
				laborDay = laborDay.AddDays(1);
			}

			return laborDay;
		}

		public static DateTime GetThanksgivingDay(int year)
		{
			var numThursdays = 0;
			var thanksDay = new DateTime(year, 10, 31);
			while (numThursdays < 4)
			{
				thanksDay = thanksDay.AddDays(1);
				if (thanksDay.DayOfWeek == DayOfWeek.Thursday)
				{
					numThursdays++;
				}
			}

			return thanksDay;
		}

		public static DateTime GetChristmasDay(int year)
		{
			return new DateTime(year, 12, 25);
		}
	}
}
