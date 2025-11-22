import random

def merge_sort(arr):
    if len(arr) <= 1:
        return arr
    mid = len(arr) // 2
    left = merge_sort(arr[:mid])
    right = merge_sort(arr[mid:])
    return merge(left, right)


def merge(left, right):
    merged = []
    while left and right:
        if left[0] <= right[0]:
            merged.append(left.pop(0))
        else:
            merged.append(right.pop(0))
    merged.extend(left if left else right)
    return merged

if __name__ == '__main__':
    arr = [random.randint(1, 100) for _ in range(20)]
    print('Original array:', arr)
    sorted_arr = merge_sort(arr)
    print('Sorted array:', sorted_arr)