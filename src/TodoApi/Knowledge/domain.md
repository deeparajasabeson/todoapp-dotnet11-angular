## Statuses
A to-do item is always in exactly one of four statuses: Pending (not started), InProgress
(being worked on), Completed (finished) and Cancelled (abandoned, will not be done).
Completed and Cancelled both count as "finished"; Pending and InProgress count as "open".

## Priorities
Priorities are ordered from least to most urgent: Low, Medium, High, Critical. Sorting by
priority orders them by that severity, not alphabetically. New items default to Medium.

## Overdue
An item is overdue when it has a due date in the past AND its status is neither Completed
nor Cancelled. An item with no due date is never overdue. Finishing or cancelling an
overdue item stops it being overdue.

## Completion timestamps
CompletedAt is set by the server the first time an item moves to Completed, and cleared
if the item is later re-opened to any other status. Clients never set it directly.

## Defaults when creating
Only a title is required. Status defaults to Pending, priority to Medium, and description
and due date are optional. Titles are limited to 200 characters and descriptions to 2000.
