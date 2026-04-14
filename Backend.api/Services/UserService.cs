using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Database;

namespace Backend.api.Services
{
    public class UserService
    {
        private WarehouseDbContext _db;
        public UserService(WarehouseDbContext db)
        {
            this._db = db;
        }

        public async Task CreateUser()
        {
            
        }

        public async Task GetUser()
        {
            
        }

        public async Task Login()
        {
            
        }

        public async Task ChangePassword()
        {
            
        }

        public async Task RequestPasswordReset()
        {
            
        }
    }
}