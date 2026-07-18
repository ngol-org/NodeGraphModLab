export class BudgetTracker {
  private used = 0;
  private readonly max: number;

  constructor() {
    this.max = parseInt(process.env.NGOL_MAX_TOOL_CALLS ?? "100");
  }

  consume(): void {
    this.used++;
    if (this.used > this.max) {
      throw new Error(
        `Tool call budget exceeded (${this.used}/${this.max}). ` +
        `Call get_budget_status to check remaining budget.`
      );
    }
  }

  statusSuffix(): string {
    const remaining = this.max - this.used;
    const warn = remaining <= 5 ? " WARNING budget low" : "";
    return `\n[budget: ${this.used}/${this.max} — ${remaining} remaining${warn}]`;
  }

  status(): string {
    return `Budget: ${this.used}/${this.max} used, ${this.max - this.used} remaining.`;
  }

  reset(): void { this.used = 0; }
}
