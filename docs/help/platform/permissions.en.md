# Permissions and permission sets

A permission is one thing somebody may do. A set is a named collection of them, and a user holds sets rather than individual permissions.

## Why sets

Because a permission list is unmaintainable at any real size. Ten modules with ten permissions each is a hundred boxes per person, and the person doing the ticking will get it wrong in the direction of granting too much. A set is a job — cashier, bookkeeper, branch manager — and hiring somebody into that job is one action.

## Reading the catalogue

Every permission arrives with the sentence its own module wrote about it, and the weighty ones are marked. They are marked rather than hidden, because hiding them would only mean granting them blind. Assembling a set from a list of keys is how somebody grants an override because the name sounded administrative.

## Sets ASAP maintains

The sets that ship — administrator, accountant, bookkeeper, setup manager, read only — are kept in step with what the installed modules declare. Installing a new module adds its permissions to the administrator set automatically, which is what stops the administrator quietly losing access to half the system on upgrade.

They cannot be edited, because an edit would be undone on the next start. Copy one to a set of your own and change that.

## The rule worth knowing

Write permissions imply read. Granting somebody the right to change customers grants the right to see them, because the alternative is a screen that refuses to show what it is about to let you edit.
