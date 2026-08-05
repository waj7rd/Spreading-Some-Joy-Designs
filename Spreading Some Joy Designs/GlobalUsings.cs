// The web project orchestrates every module, so it imports all of them rather
// than repeating the same five usings in each controller.
//
// This is deliberately only done here and in the test project. Inside the
// Domain, modules import each other explicitly — that's what makes an
// unintended dependency between modules visible in a diff.
global using SpreadingJoy.Domain.Artworks;
global using SpreadingJoy.Domain.Catalog;
global using SpreadingJoy.Domain.Identity;
global using SpreadingJoy.Domain.Ordering;
global using SpreadingJoy.Domain.Shared;
