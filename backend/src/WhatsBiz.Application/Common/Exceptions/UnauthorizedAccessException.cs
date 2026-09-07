namespace WhatsBiz.Application.Common.Exceptions;
public sealed class UnauthorizedAccessException(string message) : System.UnauthorizedAccessException(message) { }
