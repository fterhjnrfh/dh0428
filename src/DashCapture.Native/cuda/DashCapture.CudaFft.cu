#include <cuda_runtime.h>
#include <cufft.h>

#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>

#ifdef _WIN32
#define DHCAP_EXPORT extern "C" __declspec(dllexport)
#else
#define DHCAP_EXPORT extern "C" __attribute__((visibility("default")))
#endif

namespace
{
    std::mutex g_mutex;
    int g_fft_size = 0;
    int g_bin_count = 0;
    int g_batch_count = 0;
    float* g_input = nullptr;
    cufftComplex* g_spectrum = nullptr;
    float* g_magnitude = nullptr;
    cufftHandle g_plan = 0;
    bool g_has_plan = false;

    void set_error(char* error, int capacity, const char* message)
    {
        if (error == nullptr || capacity <= 0)
        {
            return;
        }

        std::snprintf(error, static_cast<size_t>(capacity), "%s", message == nullptr ? "" : message);
    }

    void set_cuda_error(char* error, int capacity, const char* operation, cudaError_t code)
    {
        char buffer[512];
        std::snprintf(buffer, sizeof(buffer), "%s failed: %s", operation, cudaGetErrorString(code));
        set_error(error, capacity, buffer);
    }

    void set_cufft_error(char* error, int capacity, const char* operation, cufftResult code)
    {
        char buffer[512];
        std::snprintf(buffer, sizeof(buffer), "%s failed: cufftResult %d", operation, static_cast<int>(code));
        set_error(error, capacity, buffer);
    }

    void release_context()
    {
        if (g_has_plan)
        {
            cufftDestroy(g_plan);
            g_has_plan = false;
            g_plan = 0;
        }

        if (g_input != nullptr)
        {
            cudaFree(g_input);
            g_input = nullptr;
        }

        if (g_spectrum != nullptr)
        {
            cudaFree(g_spectrum);
            g_spectrum = nullptr;
        }

        if (g_magnitude != nullptr)
        {
            cudaFree(g_magnitude);
            g_magnitude = nullptr;
        }

        g_fft_size = 0;
        g_bin_count = 0;
        g_batch_count = 0;
    }

    int ensure_context(int fft_size, int bin_count, int batch_count, char* error, int error_capacity)
    {
        if (fft_size <= 0 || batch_count <= 0 || bin_count != (fft_size / 2 + 1))
        {
            set_error(error, error_capacity, "Invalid FFT size, bin count, or batch count.");
            return -1;
        }

        if (g_has_plan && g_fft_size == fft_size && g_bin_count == bin_count && g_batch_count == batch_count)
        {
            return 0;
        }

        release_context();

        size_t input_count = static_cast<size_t>(fft_size) * static_cast<size_t>(batch_count);
        size_t output_count = static_cast<size_t>(bin_count) * static_cast<size_t>(batch_count);

        cudaError_t cuda_status = cudaMalloc(reinterpret_cast<void**>(&g_input), input_count * sizeof(float));
        if (cuda_status != cudaSuccess)
        {
            set_cuda_error(error, error_capacity, "cudaMalloc(input)", cuda_status);
            release_context();
            return -2;
        }

        cuda_status = cudaMalloc(reinterpret_cast<void**>(&g_spectrum), output_count * sizeof(cufftComplex));
        if (cuda_status != cudaSuccess)
        {
            set_cuda_error(error, error_capacity, "cudaMalloc(spectrum)", cuda_status);
            release_context();
            return -3;
        }

        cuda_status = cudaMalloc(reinterpret_cast<void**>(&g_magnitude), output_count * sizeof(float));
        if (cuda_status != cudaSuccess)
        {
            set_cuda_error(error, error_capacity, "cudaMalloc(magnitude)", cuda_status);
            release_context();
            return -4;
        }

        cufftResult cufft_status = cufftPlan1d(&g_plan, fft_size, CUFFT_R2C, batch_count);
        if (cufft_status != CUFFT_SUCCESS)
        {
            set_cufft_error(error, error_capacity, "cufftPlan1d", cufft_status);
            release_context();
            return -5;
        }

        g_fft_size = fft_size;
        g_bin_count = bin_count;
        g_batch_count = batch_count;
        g_has_plan = true;
        return 0;
    }
}

__global__ void magnitude_kernel(const cufftComplex* spectrum, float* magnitudes, int value_count, float scale)
{
    int index = blockIdx.x * blockDim.x + threadIdx.x;
    if (index >= value_count)
    {
        return;
    }

    cufftComplex value = spectrum[index];
    magnitudes[index] = sqrtf(value.x * value.x + value.y * value.y) * scale;
}

DHCAP_EXPORT int dc_cuda_fft_get_device_name(char* buffer, int capacity)
{
    if (buffer == nullptr || capacity <= 0)
    {
        return -1;
    }

    int count = 0;
    cudaError_t status = cudaGetDeviceCount(&count);
    if (status != cudaSuccess || count <= 0)
    {
        std::snprintf(buffer, static_cast<size_t>(capacity), "");
        return -2;
    }

    int device = 0;
    cudaGetDevice(&device);
    cudaDeviceProp prop{};
    status = cudaGetDeviceProperties(&prop, device);
    if (status != cudaSuccess)
    {
        std::snprintf(buffer, static_cast<size_t>(capacity), "");
        return -3;
    }

    std::snprintf(buffer, static_cast<size_t>(capacity), "%s", prop.name);
    return 0;
}

DHCAP_EXPORT int dc_cuda_fft_compute_magnitude_batch(
    const float* samples,
    int fft_size,
    int batch_count,
    float* magnitudes,
    int magnitude_count,
    char* error,
    int error_capacity);

DHCAP_EXPORT int dc_cuda_fft_compute_magnitude(
    const float* samples,
    int fft_size,
    float* magnitudes,
    int magnitude_count,
    char* error,
    int error_capacity)
{
    if (samples == nullptr || magnitudes == nullptr)
    {
        set_error(error, error_capacity, "Input or output pointer is null.");
        return -10;
    }

    return dc_cuda_fft_compute_magnitude_batch(samples, fft_size, 1, magnitudes, magnitude_count, error, error_capacity);
}

DHCAP_EXPORT int dc_cuda_fft_compute_magnitude_batch(
    const float* samples,
    int fft_size,
    int batch_count,
    float* magnitudes,
    int magnitude_count,
    char* error,
    int error_capacity)
{
    if (samples == nullptr || magnitudes == nullptr)
    {
        set_error(error, error_capacity, "Input or output pointer is null.");
        return -10;
    }

    int bin_count = fft_size / 2 + 1;
    if (fft_size <= 0 || batch_count <= 0 || magnitude_count != bin_count * batch_count)
    {
        set_error(error, error_capacity, "Invalid FFT batch dimensions.");
        return -15;
    }

    std::lock_guard<std::mutex> lock(g_mutex);
    int status = ensure_context(fft_size, bin_count, batch_count, error, error_capacity);
    if (status != 0)
    {
        return status;
    }

    size_t input_count = static_cast<size_t>(fft_size) * static_cast<size_t>(batch_count);
    size_t output_count = static_cast<size_t>(magnitude_count);

    cudaError_t cuda_status = cudaMemcpy(g_input, samples, input_count * sizeof(float), cudaMemcpyHostToDevice);
    if (cuda_status != cudaSuccess)
    {
        set_cuda_error(error, error_capacity, "cudaMemcpy(H2D)", cuda_status);
        return -11;
    }

    cufftResult cufft_status = cufftExecR2C(g_plan, g_input, g_spectrum);
    if (cufft_status != CUFFT_SUCCESS)
    {
        set_cufft_error(error, error_capacity, "cufftExecR2C", cufft_status);
        return -12;
    }

    int block_size = 256;
    int grid_size = (magnitude_count + block_size - 1) / block_size;
    magnitude_kernel<<<grid_size, block_size>>>(g_spectrum, g_magnitude, magnitude_count, 1.0f / static_cast<float>(fft_size));
    cuda_status = cudaGetLastError();
    if (cuda_status != cudaSuccess)
    {
        set_cuda_error(error, error_capacity, "magnitude_kernel", cuda_status);
        return -13;
    }

    cuda_status = cudaMemcpy(magnitudes, g_magnitude, output_count * sizeof(float), cudaMemcpyDeviceToHost);
    if (cuda_status != cudaSuccess)
    {
        set_cuda_error(error, error_capacity, "cudaMemcpy(D2H)", cuda_status);
        return -14;
    }

    return 0;
}

DHCAP_EXPORT void dc_cuda_fft_dispose()
{
    std::lock_guard<std::mutex> lock(g_mutex);
    release_context();
}
