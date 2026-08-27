# How permissions are decided

Every command and query in ASAP declares the permission it needs. The check happens once, at the point the request is dispatched, and not again in the screen — which is why a screen and an endpoint cannot disagree about what somebody may do.

## What a user actually holds

The permissions from every set assigned to them, plus everything those permissions imply. A write implies its read.

A superuser passes every check without holding anything. That is what a superuser is, and it is why the sets on such an account describe nothing about what it can do.

## What a refusal says

The permission that was needed, and what the caller was trying to do. Not "access denied": somebody who is refused needs to know what to ask for, and their manager needs to know what to grant.

## Overrides are not permissions to ignore rules

Some rules can be pushed past by somebody holding an override permission — posting to a control account, selling below the margin floor, going over a credit limit. Every one of them is recorded in the audit log against the person's name, with the reason they gave.

An override permission is therefore not a way to make a rule stop applying. It is a way to make the rule ask who is doing it and why, which is a different and much better thing.

## The menu

The menu is filtered to what the caller may actually open, so nobody is shown a screen that will refuse them on arrival. A cashier's menu is five entries rather than forty with thirty-five dead ends.
