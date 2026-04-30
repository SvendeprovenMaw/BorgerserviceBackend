using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Entities.Dto;

namespace Backend.api.Extensions
{
    public static class BinaryFileHelper
    {
        /// <summary>
        /// helps convert IFormFile to BinaryData
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
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
        /// <summary>
        /// helps quickly convert FileUploadDto to binary data for use in ai processing
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        public static async Task<List<BinaryData>> ToBinaryDataListAsync(this IEnumerable<FileUploadDto> dto)
        {
            var binaryDataList = new List<BinaryData>();

            foreach (var file in dto.Where(f => f != null && f.File.Length > 0))
            {
                using var stream = file.File.OpenReadStream();
                binaryDataList.Add(await BinaryData.FromStreamAsync(stream));
            }

            return binaryDataList;
        }
    }
}