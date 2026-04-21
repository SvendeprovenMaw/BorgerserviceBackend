using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Extensions
{
    public static class BinaryFileHelper
    {

        public static async Task<BinaryData> ToBinaryData(IFormFile file)
        {
            using var stream = file.OpenReadStream();
            return await BinaryData.FromStreamAsync(stream);
        }
        public static async Task<List<BinaryData>> ToBinaryDataListAsync(this IEnumerable<IFormFile> files)
        {
            var binaryDataList = new List<BinaryData>();

            foreach (var file in files.Where(f => f != null && f.Length > 0))
            {
                using var stream = file.OpenReadStream();
                binaryDataList.Add(await BinaryData.FromStreamAsync(stream));
            }

            return binaryDataList;
        }
    }
}