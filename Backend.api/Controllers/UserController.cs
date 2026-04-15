using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Entities.Dto;
using Backend.api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private IUserService _UserService;
        public UserController(IUserService userService)
        {
            this._UserService = userService;
        }
        [HttpPost]
        public async Task<IActionResult> RegisterUser(CreateUserDto createUserDto)
        {
            if(await _UserService.CreateUser(createUserDto))
            {
                return Created();
            }

            return NoContent();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto loginDto)
        {
            var User = await _UserService.Login(loginDto);
            if(User == null)
            {
                return NotFound();
            }

            if(User.Password == PasswordHasher.Hash(loginDto.Password, ""))
            {
                return Ok(new {jwt ="Jwt-Token"});
            }

            return NoContent();
        }
    }
}