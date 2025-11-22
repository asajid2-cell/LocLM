def stalin_sort(arr):
    result = [arr[0]]
    for i in range(1, len(arr)):
        if arr[i] >= result[-1]:
            result.append(arr[i])
    return result

if __name__ == '__main__':
    print(stalin_sort([1, 3, 2, 4, 54,543,2,12,3445,6677,765,3434565,25]))