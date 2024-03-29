﻿using System;
using System.Text;
using System.Threading.Tasks;
using Waher.Script;

namespace XamarinApp.PersonalNumbers
{
	/// <summary>
	/// Checks personal numbers against a personal number scheme.
	/// </summary>
	public class PersonalNumberScheme
	{
		private readonly string variableName;
		private readonly string displayString;
		private readonly Expression pattern;
		private readonly Expression check;
		private readonly Expression normalize;

		/// <summary>
		/// Checks personal numbers against a personal number scheme.
		/// </summary>
		/// <param name="VariableName">Name of variable to use in script for the personal number.</param>
		/// <param name="DisplayString">A string that can be displayed to a user, informing the user about the approximate format expected.</param>
		/// <param name="Pattern">Expression checking if the scheme applies to a personal number.</param>
		/// <param name="Check">Optional expression, checking if the contents of the personal number is valid.</param>
		/// <param name="Normalize">Optional normalization expression.</param>
		public PersonalNumberScheme(string VariableName, string DisplayString, Expression Pattern, Expression Check, Expression Normalize)
		{
			this.variableName = VariableName;
			this.displayString = DisplayString;
			this.pattern = Pattern;
			this.check = Check;
			this.normalize = Normalize;
		}

		/// <summary>
		/// A string that can be displayed to a user, informing the user about the approximate format expected.
		/// </summary>
		public string DisplayString => this.displayString;

		/// <summary>
		/// Checks if a personal number is valid according to the personal number scheme.
		/// </summary>
		/// <returns>Validation information about the number.</returns>
		public async Task<NumberInformation> Validate(string PersonalNumber)
		{
			NumberInformation Info = new NumberInformation()
			{
				PersonalNumber = PersonalNumber,
				DisplayString = string.Empty
			};

			try
			{
				Variables Variables = new Variables(new Variable(this.variableName, PersonalNumber));
				object EvalResult = await this.pattern.EvaluateAsync(Variables);

				if (EvalResult is bool b)
				{
					if (!b)
						return null;

					if (!(this.check is null))
					{
						EvalResult = await this.check.EvaluateAsync(Variables);

						if (!(EvalResult is bool b2) || !b2)
						{
							Info.IsValid = false;
							return Info;
						}
					}

					if (!(this.normalize is null))
					{
						EvalResult = await this.normalize.EvaluateAsync(Variables);

						if (!(EvalResult is string Normalized))
						{
							Info.IsValid = false;
							return Info;
						}

						Info.PersonalNumber = Normalized;
					}

					Info.IsValid = true;
					return Info;
				}
				else
				{
					Info.IsValid = null;
					return Info;
				}
			}
			catch (Exception)
			{
				Info.IsValid = false;
				return Info;
			}
		}
	}
}
