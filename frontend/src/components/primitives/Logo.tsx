interface LogoProps {
  size?: number;
  className?: string;
}

/**
 * Dataverse AI Forge brand mark — "Molten Cell": 8 solid cells (the 8 agents)
 * around a forged teal core, on a white squircle tile. Identical to the desktop
 * dock / favicon icon so the brand reads the same everywhere.
 * Canonical source: frontend/build-resources/app-icon.svg.
 */
export function Logo({ size = 28, className }: LogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 96 96"
      className={className}
      role="img"
      aria-label="Dataverse AI Forge"
    >
      <rect width="96" height="96" rx="22" fill="#ffffff" />
      <g fill="#dfe3e8">
        <rect x="6" y="6" width="24" height="24" rx="4" />
        <rect x="36" y="6" width="24" height="24" rx="4" />
        <rect x="66" y="6" width="24" height="24" rx="4" />
        <rect x="6" y="36" width="24" height="24" rx="4" />
        <rect x="66" y="36" width="24" height="24" rx="4" />
        <rect x="6" y="66" width="24" height="24" rx="4" />
        <rect x="36" y="66" width="24" height="24" rx="4" />
        <rect x="66" y="66" width="24" height="24" rx="4" />
      </g>
      <rect x="33" y="33" width="30" height="30" rx="5" fill="#0f8a7a" />
      <rect x="41" y="41" width="14" height="14" rx="2.5" fill="#2fb3a1" />
    </svg>
  );
}
