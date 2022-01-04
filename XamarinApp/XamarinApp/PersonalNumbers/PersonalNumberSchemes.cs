using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
					typeof(PersonalNumberSchemes).Namespace + ".PersonalNumberSchemes.xml")))
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
						Expression Normalize = null;

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

										case "Normalize":
											Normalize = new Expression(E2.InnerText);
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

						Schemes.AddLast(new PersonalNumberScheme(Variable, DisplayString, Pattern, Check, Normalize));
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
		/// <param name="DisplayString">A string that can be displayed to a user, informing the user about the approximate format expected.</param>
		/// <returns>Validation information about the number.</returns>
		public static async Task<NumberInformation> Validate(string CountryCode, string PersonalNumber)
		{
			if (schemesByCode.TryGetValue(CountryCode, out LinkedList<PersonalNumberScheme> Schemes))
			{
				foreach (PersonalNumberScheme Scheme in Schemes)
				{
					NumberInformation Info = await Scheme.Validate(PersonalNumber);
					if (Info.IsValid.HasValue)
					{
						Info.DisplayString = Scheme.DisplayString;
						return Info;
					}
				}

				return new NumberInformation()
				{
					PersonalNumber = PersonalNumber,
					DisplayString = string.Empty,
					IsValid = false
				};
			}
			else
			{
				return new NumberInformation()
				{
					PersonalNumber = PersonalNumber,
					DisplayString = string.Empty,
					IsValid = null
				};
			}
		}
	}
}
