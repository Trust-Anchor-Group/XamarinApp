using System;

namespace XamarinApp.PersonalNumbers
{
	/// <summary>
	/// Personal number information
	/// </summary>
	public class NumberInformation
	{
		/// <summary>
		/// String representation of the personal number.
		/// </summary>
		public string PersonalNumber;

		/// <summary>
		/// true = valid: <paramref name="PersonalNumber"/> may be normalized.
		/// false = invalid
		/// null = scheme not applicable
		/// </summary>
		public bool? IsValid;

		/// <summary>
		/// A string that can be displayed to a user, informing the user about the approximate format expected.
		/// </summary>
		public string DisplayString;
	}
}
