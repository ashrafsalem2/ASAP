# User accounts

An account is one person. Two people sharing one is how an audit log stops being able to say who did anything, which is the only reason it exists.

## Creating one

An administrator chooses the first password so it can be handed over in person, and the account is marked to change it. A password somebody else knows is a shared secret until it has been used once; after that it is theirs.

Passwords are at least twelve characters. Length rather than a rule about symbols: a rule demanding a symbol produces the same predictable password on every installation that has one, and length is what actually costs an attacker anything.

## Turning an account off

Accounts are turned off, not deleted. The audit log points at them, last year's documents were posted by them, and a deleted account leaves a trail nobody can follow.

Two things are refused. You cannot turn off the account you are signed in with — the rule exists so that nobody removes their own way back in and then discovers it. And nothing may leave the installation without an account able to administer users and permissions: there is no way back from that which does not involve a database, so there is nothing for a permission to unlock.

## Superusers

A superuser passes every permission check. The permission sets on such an account describe nothing about what it can actually do, and the screen says so. Keep the number of them at one, and use permission sets for everybody else — including the people who think they need everything.

## Resetting a password

A reset also clears a lockout. Somebody locked out after five wrong attempts is usually somebody who needs a reset, and an administrator doing the obvious thing should not find it appeared not to work.
