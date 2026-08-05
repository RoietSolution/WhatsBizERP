using FluentValidation;
namespace WhatsBiz.Application.Features.Authentication.RefreshToken;
public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand> { public RefreshTokenCommandValidator() { RuleFor(x => x.Token).NotEmpty().MaximumLength(512); } }
