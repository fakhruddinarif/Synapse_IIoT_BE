using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Enums
{
    public enum DataTypeTable
    {
        STRING = 0, // Represents a sequence of characters. It is used to store text data.
        INTEGER = 1, // Represents whole numbers without decimal points. It is used to store numeric data that does not require fractional values.
        FLOAT = 2, // Represents numbers with decimal points. It is used to store numeric data that requires fractional values.
        BOOLEAN = 3, // Represents a binary value of true or false. It is used to store logical data that can only have two possible values.
        DATETIME = 4, // Represents a specific point in time, including date and time components. It is used to store temporal data.
    }
}