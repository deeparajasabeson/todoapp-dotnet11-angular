## Endpoints
The REST API is at /api/todos. GET /api/todos lists items. GET /api/todos/{id} fetches one.
POST /api/todos creates one. PUT /api/todos/{id} replaces one. PATCH /api/todos/{id}/status
changes only the status. PATCH /api/todos/{id}/priority changes only the priority.
DELETE /api/todos/{id} removes one.

## Filtering and sorting
GET /api/todos accepts status, priority, minPriority, search, isOverdue, dueBefore, sortBy,
descending, page and pageSize. minPriority means "this priority or higher". search matches
title and description case-insensitively. sortBy accepts CreatedAt, UpdatedAt, DueDate,
Priority, Status and Title. Items with no due date always sort last when sorting by due date.

## Paging
Responses are paged: the body has items, page, pageSize, totalCount, totalPages,
hasPreviousPage and hasNextPage. pageSize defaults to 20 and is capped at 200.

## Errors
Failures return RFC 9457 problem details. A 400 carries an errors map keyed by field name
for validation failures. A 404 means no item has that id. Request bodies are validated with
FluentValidation before the handler runs.

## Exploring the API
Swagger UI is available at /swagger while running in Development, with try-it-out enabled.
The OpenAPI document is at /openapi/v1.json.
