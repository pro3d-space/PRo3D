namespace PRo3DCompability
{
    using Aardvark.Base;
    using System;
    using System.Linq;
    using System.Collections.Generic;


    public static class PRo3DCSharp
    {
        public static int[] ComputeIndexArray(V2i size, IEnumerable<int> invalidPoints)
        {
            var indexArray = new int[(size.X - 1) * (size.Y - 1) * 6];

            int k = 0;

            bool hasInvalids = invalidPoints != null;

            #region no invalids
            if (!hasInvalids)
            {
                for (int y = 0; y < size.Y - 1; y++)
                {
                    for (int x = 0; x < size.X - 1; x++)
                    {
                        indexArray[k] = y * size.X + x;
                        indexArray[k + 1] = (y + 1) * size.X + x;
                        indexArray[k + 2] = y * size.X + x + 1;

                        indexArray[k + 3] = y * size.X + x + 1;
                        indexArray[k + 4] = (y + 1) * size.X + x;
                        indexArray[k + 5] = (y + 1) * size.X + x + 1;

                        k += 6;
                    }
                }

                return indexArray.ToArray();
            }
            #endregion
            #region has invalids
            else
            {
                var invalidDict = invalidPoints.ToDictionary(n => n);
                int a1, b1, c1;
                int a2, b2, c2;
                var indices = new List<int>();
                bool invalidFace = false;
                int counter = 0;

                for (int y = 0; y < size.Y - 1; y++)
                {
                    for (int x = 0; x < size.X - 1; x++)
                    {
                        a1 = y * size.X + x;
                        b1 = (y + 1) * size.X + x;
                        c1 = y * size.X + x + 1;

                        a2 = y * size.X + x + 1;
                        b2 = (y + 1) * size.X + x;
                        c2 = (y + 1) * size.X + x + 1;

                        indices.Clear();
                        indices.Add(a1); indices.Add(b1); indices.Add(c1);
                        indices.Add(a2); indices.Add(b2); indices.Add(c2);

                        invalidFace = indices.Select(n => n).Where(m => invalidDict.ContainsKey(m)).ToList().Count() > 0;

                        if (invalidFace)
                        {
                            //Console.WriteLine("Invalid Face Found");
                            counter++;
                            k += 6;
                            continue;
                        }

                        indexArray[k] = a1;
                        indexArray[k + 1] = b1;
                        indexArray[k + 2] = c1;

                        indexArray[k + 3] = a2;
                        indexArray[k + 4] = b2;
                        indexArray[k + 5] = c2;

                        k += 6;
                    }
                }

                if (counter > 0)
                    Report.Line(5, "Invalid faces found: " + counter);
                return indexArray.ToArray();
            }
            #endregion
        }

        
    }
}