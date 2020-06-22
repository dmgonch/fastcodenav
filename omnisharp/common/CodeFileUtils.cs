using System;
using System.IO;
using System.Linq;

namespace FastCodeNavPlugin.Common
{
    public static class CodeFileUtils
    {
        // Use very simplistic parsing to locate a symbol at a given position in a file
        public static bool TryGetSymbolTextFromFile(string filePath, int lineIndex, int columnIndex, out string symbolText)
        {
            symbolText = null;
            string line = File.ReadAllLines(filePath).Skip(lineIndex).Take(1).FirstOrDefault();

            if (line == null)
            {
                // The file was potentially changed in the editor but hasn't been saved yet
                return false;
            }

            // Do the very basic check to rule out looking for a code symbol in a single line comment
            if (line.Trim().Length >= 2 && line.Trim().Substring(0, 2) == @"//")
            {
                // Either the file was potentially changed in the editor but hasn't been saved yet or user just pressed F12 inside of a comment
                return false;
            }

            // Locate where the symbol text starts
            int startPosition = columnIndex;
            do
            {
                startPosition--;
            } while (startPosition >= 0 && Char.IsLetter(line[startPosition]));
            startPosition++;

            // Locate where the symbol text ends
            int endPosition = columnIndex;
            while (endPosition < line.Length && Char.IsLetterOrDigit(line[endPosition]))
            {
                endPosition++;
            }

            symbolText = line.Substring(startPosition, endPosition - startPosition);
            return symbolText.Length > 0;
        }
    }
}
