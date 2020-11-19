using System;
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

		/// <summary>
		/// Checks personal numbers against a personal number scheme.
		/// </summary>
		/// <param name="VariableName">Name of variable to use in script for the personal number.</param>
		/// <param name="DisplayString">A string that can be displayed to a user, informing the user about the approximate format expected.</param>
		/// <param name="Pattern">Expression checking if the scheme applies to a personal number.</param>
		public PersonalNumberScheme(string VariableName, string DisplayString, Expression Pattern)
			: this(VariableName, DisplayString, Pattern, null)
		{
		}

		/// <summary>
		/// Checks personal numbers against a personal number scheme.
		/// </summary>
		/// <param name="VariableName">Name of variable to use in script for the personal number.</param>
		/// <param name="DisplayString">A string that can be displayed to a user, informing the user about the approximate format expected.</param>
		/// <param name="Pattern">Expression checking if the scheme applies to a personal number.</param>
		/// <param name="Check">Optional expression, checking if the contents of the personal number is valid.</param>
		public PersonalNumberScheme(string VariableName, string DisplayString, Expression Pattern, Expression Check)
		{
			this.variableName = VariableName;
			this.displayString = DisplayString;
			this.pattern = Pattern;
			this.check = Check;
		}

		/// <summary>
		/// A string that can be displayed to a user, informing the user about the approximate format expected.
		/// </summary>
		public string DisplayString => this.displayString;

		/// <summary>
		/// Checks if a personal number is valid according to the personal number scheme.
		/// </summary>
		/// <param name="PersonalNumber">String representation of the personal number.</param>
		/// <returns>
		/// true = valid
		/// false = invalid
		/// null = scheme not applicable
		/// </returns>
		public bool? IsValid(string PersonalNumber)
		{
			try
			{
				Variables Variables = new Variables(new Variable(this.variableName, PersonalNumber));
				object Result = this.pattern.Evaluate(Variables);

				if (Result is bool b)
				{
					if (!b)
						return null;

					if (!(this.check is null))
					{
						Result = this.check.Evaluate(Variables);
						return Result is bool b2 && b2;
					}
					else
						return true;
				}
				else
					return null;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}
}
