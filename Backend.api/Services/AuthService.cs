using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Database;
using Backend.api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services
{
    public interface IAuthService
    {
        Task SaveRefreshToken(RefreshToken refreshToken);
        public Task<RefreshToken?> GetRefreshToken(string token);
        public Task RevokeToken(RefreshToken token);
    }

    public class AuthService : IAuthService
    {
        private readonly ApplyAiDbContext _db;
        public AuthService(ApplyAiDbContext db)
        {
            this._db = db;
        }
        public async Task SaveRefreshToken(RefreshToken refreshToken)
        {
            await this._db.AddAsync(refreshToken);
            await _db.SaveChangesAsync();
        }

        public async Task<RefreshToken?> GetRefreshToken(string token)
        {
            return await _db.RefreshTokens.Include(i=>i.User).AsNoTracking()
            .FirstOrDefaultAsync(i=>i.Token == token);
        }

        public async Task RevokeToken(RefreshToken token)
        {
            await _db.RefreshTokens.Where(i=>i.Id == token.Id).ExecuteDeleteAsync();
        }
    }
}