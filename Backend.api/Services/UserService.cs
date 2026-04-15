using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using JwtLibrary;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services
{
    public interface IUserService
    {
        Task ChangePassword();
        Task<bool> CreateUser(CreateUserDto createUserDto);
        Task GetUser();
        Task GetUserProfile();
        Task HardDeleteAccount();
        Task<User?> Login(LoginDto loginDto);
        Task RequestPasswordReset();
    }

    public class UserService : IUserService
    {
        private WarehouseDbContext _db;
        public UserService(WarehouseDbContext db)
        {
            this._db = db;
        }

        public async Task<bool> CreateUser(CreateUserDto createUserDto)
        {
            var result = await _db.Users.Where(i => i.Username == createUserDto.Username || i.Email == createUserDto.Email).AnyAsync();
            if (result) { return false; }
            User user = new(JwtRoles.User, createUserDto.Email, createUserDto.Username, PasswordHasher.Hash(createUserDto.Password, ""));
            await _db.AddAsync(user);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task GetUser()
        {

        }

        public async Task<User?> Login(LoginDto loginDto)
        {
            return await _db.Users.Where(i => i.Username == loginDto.Username || i.Email == loginDto.Username).FirstOrDefaultAsync();
        }

        public async Task ChangePassword()
        {

        }

        public async Task RequestPasswordReset()
        {

        }

        public async Task HardDeleteAccount()
        {

        }

        public async Task GetUserProfile()
        {

        }
    }
}