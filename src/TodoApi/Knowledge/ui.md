## The web UI
The Angular app lists to-do items on the left two thirds of the screen and this chat panel
on the right third. The list has a search box, a status filter, a minimum-priority filter,
a sort selector, an ascending/descending toggle and an "overdue only" checkbox.

## Working with items in the UI
Each row has a checkbox that completes or re-opens the item, dropdowns that change status
and priority directly, an Edit button for inline editing of title, notes, status, priority
and due date, and a Delete button that asks for confirmation. The "+ New todo" button opens
a composer at the top of the list.

## What this assistant can do
The assistant can list, search, count, create, re-prioritise, re-status and delete items,
and answer questions about how the app and its API work. Changes made through chat appear
in the list immediately, because both go through the same service and database.
