# Upgrade Options

## Strategy

### Upgrade Strategy

| Value | Description |
|-------|-------------|
| **All-at-Once** (selected) | Upgrade all 3 projects (Shared, Client, Server) together in one pass — small solution, all net8.0, SDK-style. |
| Bottom-Up | Upgrade Shared, then Client, then Server, validating each tier. |
| Top-Down | Upgrade Server first, multi-target libraries as needed. |
