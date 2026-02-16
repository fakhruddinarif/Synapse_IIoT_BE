using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Enums
{
	public enum DataType
	{
		BOOLEAN, // Represents a true or false value. Used for binary states, such as on/off or enabled/disabled.
		INT16, // Represents a 16-bit signed integer. Used for small numeric values, such as counts or small measurements.
		UINT16, // Represents a 16-bit unsigned integer. Used for small numeric values that cannot be negative, such as counts or small measurements.
		INT32, // Represents a 32-bit signed integer. Used for larger numeric values, such as counts or measurements that can be negative.
		UINT32, // Represents a 32-bit unsigned integer. Used for larger numeric values that cannot be negative, such as counts or measurements.
		FLOAT, // Represents a single-precision floating-point number. Used for measurements that require decimal precision, such as temperature or pressure.
		STRING // Represents a sequence of characters. Used for textual data, such as names, descriptions, or status messages.
	}
}
