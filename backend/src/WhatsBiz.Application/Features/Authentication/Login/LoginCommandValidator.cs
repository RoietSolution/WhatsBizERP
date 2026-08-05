using FluentValidation;
namespace WhatsBiz.Application.Features.Authentication.Login;
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand> { public LoginCommandValidator() { RuleFor(x => x.Username).NotEmpty().MaximumLength(256); RuleFor(x => x.Password).NotEmpty().MaximumLength(256); } }
