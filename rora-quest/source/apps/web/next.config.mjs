/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // Required for Docker standalone output
  output: "standalone",
};

export default nextConfig;

