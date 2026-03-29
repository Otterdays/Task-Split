<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Style Guide: TaskSplit Project

## Coding Standards
- **Naming**: `camelCase` (Local variables) • `PascalCase` (Public properties, Methods, Classes) • `_camelCase` (Private fields).
- **Formatting**: Use 4-space indentation. Keep lines under 100 characters.
- **Async**: Favor `async/await` for UI and I/O tasks.
- **Traceability**: All source files MUST contain a `// [TRACE: file.md]` header pointing to relevant documentation.

## Commit Conventions
- **Format**: `<type>(<scope>): <description>`
- **Types**: `feat` (New feature), `fix` (Bug fix), `docs` (Documentation changes), `style` (Formatting), `refactor` (Refactoring), `test` (Tests), `chore` (Build changes).
- **Example**: `feat(overlay): add group divider rendering logic`

## C# Specifics
- Avoid `null` whenever possible. Use `?` for nullable types and handle nullability explicitly.
- Use `Expression-bodied members` (e.g., `=>`) for short properties and methods.
- Document all public APIs with `<summary>` tags.
