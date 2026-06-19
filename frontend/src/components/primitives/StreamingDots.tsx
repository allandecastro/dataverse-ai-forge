"use client";

interface StreamingDotsProps {
  color?: string;
  size?: number;
}

export function StreamingDots({ color = "bg-t-3", size = 1.5 }: StreamingDotsProps) {
  return (
    <div className="flex gap-0.5 items-center">
      {[0, 1, 2].map((i) => (
        <span
          key={i}
          className={`block rounded-full ${color}`}
          style={{
            width: `${size * 4}px`,
            height: `${size * 4}px`,
            animation: "streaming-dots 1.4s infinite ease-in-out both",
            animationDelay: `${i * 0.16}s`,
          }}
        />
      ))}
    </div>
  );
}
