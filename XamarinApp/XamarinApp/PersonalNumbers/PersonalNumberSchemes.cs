using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Waher.Content.Xml;
using Waher.Events;
using Waher.Script;

namespace XamarinApp.PersonalNumbers
{
	/// <summary>
	/// Personal Number Schemes available in different countries.
	/// </summary>
	public static class PersonalNumberSchemes
	{
		private static readonly Dictionary<string, LinkedList<PersonalNumberScheme>> schemesByCode = new Dictionary<string, LinkedList<PersonalNumberScheme>>();

		internal static void Load()
		{
			try
			{
				XmlDocument Doc = new XmlDocument();

				using (MemoryStream ms = new MemoryStream(Waher.Content.Resources.LoadResource(
					typeof(PersonalNumberSchemes).Namespace + ".PersonalNumbersSchemes.xml")))
				{
					Doc.Load(ms);
				}

				foreach (XmlNode N in Doc.DocumentElement.ChildNodes)
				{
					if (N is XmlElement E && E.LocalName == "Entry")
					{
						string Country = XML.Attribute(E, "country");
						string DisplayString = XML.Attribute(E, "displayString");
						string Variable = null;
						Expression Pattern = null;
						Expression Check = null;

						try
						{
							foreach (XmlNode N2 in E.ChildNodes)
							{
								if (N2 is XmlElement E2)
								{
									switch (E2.LocalName)
									{
										case "Pattern":
											Pattern = new Expression(E2.InnerText);
											Variable = XML.Attribute(E2, "variable");
											break;

										case "Check":
											Check = new Expression(E2.InnerText);
											break;
									}
								}
							}
						}
						catch (Exception ex)
						{
							Log.Critical(ex);
							continue;
						}

						if (Pattern is null || string.IsNullOrEmpty(Variable) || string.IsNullOrEmpty(DisplayString))
							continue;

						if (!schemesByCode.TryGetValue(Country, out LinkedList<PersonalNumberScheme> Schemes))
						{
							Schemes = new LinkedList<PersonalNumberScheme>();
							schemesByCode[Country] = Schemes;
						}

						Schemes.AddLast(new PersonalNumberScheme(Variable, DisplayString, Pattern, Check));
					}
				}
			}
			catch (Exception ex)
			{
				Log.Critical(ex);
			}
		}

		/// <summary>
		/// Checks if a personal number is valid, in accordance with registered personal number schemes.
		/// </summary>
		/// <param name="Country">ISO 3166-1 Country Codes.</param>
		/// <param name="PersonalNumber">Personal Number</param>
		/// <returns>
		/// true = valid
		/// false = invalid
		/// null = no registered schemes for country.
		/// </returns>
		public static bool? IsValid(string CountryCode, string PersonalNumber)
		{
			return IsValid(CountryCode, PersonalNumber, out string _);
		}

		/// <summary>
		/// Checks if a personal number is valid, in accordance with registered personal number schemes.
		/// </summary>
		/// <param name="Country">ISO 3166-1 Country Codes.</param>
		/// <param name="PersonalNumber">Personal Number</param>
		/// <param name="DisplayString">A string that can be displayed to a user, informing the user about the approximate format expected.</param>
		/// <returns>
		/// true = valid
		/// false = invalid
		/// null = no registered schemes for country.
		/// </returns>
		public static bool? IsValid(string CountryCode, string PersonalNumber, out string DisplayString)
		{
			DisplayString = string.Empty;

			if (schemesByCode.TryGetValue(CountryCode, out LinkedList<PersonalNumberScheme> Schemes))
			{
				foreach (PersonalNumberScheme Scheme in Schemes)
				{
					if (string.IsNullOrEmpty(DisplayString))
						DisplayString = Scheme.DisplayString;

					bool? Valid = Scheme.IsValid(PersonalNumber);
					if (Valid.HasValue)
						return Valid;
				}

				return false;
			}
			else
				return null;
		}
	}
}
