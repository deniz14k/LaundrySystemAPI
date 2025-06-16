using System;
using System.Collections.Generic;
using System.Linq;
using ApiSpalatorie.Helpers;
using ApiSpalatorie.Models;
using ApiSpalatorie.Data;
using ApiSpalatorie.Controllers;

namespace ApiSpalatorie.Helpers
{
    public static class TspRouteOptimizer
    {
        public static List<int> SolveTsp(List<(double lat, double lng)> coords)
        {
            int n = coords.Count;
            var matrix = new double[n, n];

            // Step 1: Distance matrix using Haversine
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    matrix[i, j] = i == j ? 0 : Haversine(
                        coords[i].lat, coords[i].lng,
                        coords[j].lat, coords[j].lng);

            // Step 2: Nearest Neighbor
            var path = NearestNeighbor(matrix);

            // Step 3: 2-opt improvement
            return TwoOpt(path, matrix);
        }

        private static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371e3;
            double φ1 = lat1 * Math.PI / 180;
            double φ2 = lat2 * Math.PI / 180;
            double Δφ = (lat2 - lat1) * Math.PI / 180;
            double Δλ = (lon2 - lon1) * Math.PI / 180;

            double a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) +
                       Math.Cos(φ1) * Math.Cos(φ2) *
                       Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c; // meters
        }

        private static List<int> NearestNeighbor(double[,] matrix)
        {
            int n = matrix.GetLength(0);
            bool[] visited = new bool[n];
            var route = new List<int> { 0 };
            visited[0] = true;

            while (route.Count < n)
            {
                int last = route.Last();
                double best = double.MaxValue;
                int next = -1;

                for (int i = 0; i < n; i++)
                {
                    if (!visited[i] && matrix[last, i] < best)
                    {
                        best = matrix[last, i];
                        next = i;
                    }
                }

                visited[next] = true;
                route.Add(next);
            }

            return route;
        }

        private static List<int> TwoOpt(List<int> path, double[,] matrix)
        {
            bool improved = true;
            int n = path.Count;

            while (improved)
            {
                improved = false;
                for (int i = 1; i < n - 2; i++)
                {
                    for (int j = i + 1; j < n - 1; j++)
                    {
                        double before = matrix[path[i - 1], path[i]] + matrix[path[j], path[j + 1]];
                        double after = matrix[path[i - 1], path[j]] + matrix[path[i], path[j + 1]];

                        if (after < before)
                        {
                            path.Reverse(i, j - i + 1);
                            improved = true;
                        }
                    }
                }
            }

            return path;
        }
    }
}
